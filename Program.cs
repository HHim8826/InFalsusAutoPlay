using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using OpCodes = dnlib.DotNet.Emit.OpCodes;

namespace In_Falsus_auto_play;

/// <summary>
/// Auto-Play Patcher for a heavily obfuscated Unity rhythm game.
/// Targets class $e.$ad and patches three core judgment methods:
///   - $Qt (Flick/Slide notes)
///   - $st (SkyArea/Follow notes — lowercase s)
///   - $St (Tap & Hold notes — uppercase S)
/// </summary>
internal static class Program
{
	// ──────────────────── configuration ────────────────────
	// Modify this to point at the game's managed assembly.
	private const string DefaultInputDll = "Game.dll";

	// Obfuscated identifiers — single source of truth
	private const string NS_e = "$e";
	private const string CLS_ad = "$ad";
	private const string NS_A = "$A";
	private const string TYPE_X = "$X";
	private const string TYPE_ed = "$ed";
	private const string TYPE_Fd = "$Fd";
	private const string TYPE_XC = "$XC";
	private const string TYPE_aA = "$aA";

	private const string FLD_md = "$md";  // float64 — time difference
	private const string FLD_pd = "$pd";  // int32 — lane start
	private const string FLD_Pd = "$Pd";  // int32 — lane end
	private const string FLD_Id = "$Id";  // $aA — note type
	private const string FLD_px = "$px";  // bool — physical key pressed
	private const string FLD_Vx = "$Vx";  // bool — internal hold tracking
	private const string FLD_vx = "$vx";  // bool — internal hold tracking
	private const string FLD_fW = "$fW";  // $ed[] — key state array
	private const string FLD_SW = "$SW";  // $XC — input state struct
	private const string FLD_od = "$od";  // bool — note done flag
	private const string FLD_hd = "$hd";  // int64 — note id

	private const int NOTE_TYPE_HOLD = 2; // $aA.$hD

	static void Main(string[] args)
	{
		string inputPath = args.Length > 0 ? args[0] : DefaultInputDll;
		if (!File.Exists(inputPath))
		{
			Console.Error.WriteLine($"[ERROR] File not found: {inputPath}");
			Console.Error.WriteLine("Usage: In_Falsus_auto_play <path-to-Game.dll>");
			return;
		}

		string outputPath = Path.Combine(
			Path.GetDirectoryName(inputPath)!,
			Path.GetFileNameWithoutExtension(inputPath) + "_patched" + Path.GetExtension(inputPath));

		Console.WriteLine($"[*] Loading {inputPath} …");
		using var module = ModuleDefMD.Load(inputPath);

		// Locate the core class $e.$ad
		TypeDef adClass = module.Find($"{NS_e}.{CLS_ad}", isReflectionName: true)
			?? throw new Exception($"Class {NS_e}.{CLS_ad} not found!");

		Console.WriteLine($"[+] Found class {adClass.FullName}");

		// Resolve external type refs we'll need
		TypeDef typeX = FindTypeDef(module, NS_A, TYPE_X);
		TypeDef typeEd = FindTypeDef(module, NS_e, TYPE_ed);
		TypeDef typeFd = FindTypeDef(module, NS_e, TYPE_Fd);
		TypeDef typeXC = FindTypeDef(module, NS_e, TYPE_XC);

		// Field references
		FieldDef fld_md = FindField(typeX, FLD_md);
		FieldDef fld_pd = FindField(typeX, FLD_pd);
		FieldDef fld_Pd = FindField(typeX, FLD_Pd);
		FieldDef fld_Id = FindField(typeX, FLD_Id);
		FieldDef fld_od = FindField(typeX, FLD_od);
		FieldDef fld_px = FindField(typeEd, FLD_px);
		FieldDef fld_Vx = FindField(typeFd, FLD_Vx);
		FieldDef fld_vx = FindField(typeFd, FLD_vx);
		FieldDef fld_fW = FindField(typeXC, FLD_fW);
		FieldDef fld_SW = FindField(adClass, FLD_SW);

		// System.Math::Abs(float64) — reuse the reference already present in the game's IL
		// Do NOT use module.Import(typeof(Math)...) as that imports from System.Private.CoreLib
		// while the game uses [mscorlib]System.Math.
		var mathAbs = FindExistingMathAbs(adClass)
			?? throw new Exception("Cannot find existing System.Math::Abs(float64) reference in $ad methods!");

		// ─── Patch A: $Qt — Flick/Slide auto-perfect ───
		PatchQt(adClass, fld_md, mathAbs);

		// ─── Patch B: $st — SkyArea/Follow auto-perfect ───
		PatchSt_lower(adClass, fld_Vx, fld_vx);

		// ─── Patch C: $St — Tap & Hold auto-perfect + visuals ───
		PatchSt_upper(adClass, module, fld_md, fld_pd, fld_Pd, fld_Id, fld_px, fld_fW, fld_SW, fld_od, mathAbs);

		Console.WriteLine($"\n[*] Writing patched assembly to {outputPath} …");
		module.Write(outputPath);
		Console.WriteLine("[+] Done! All patches applied successfully.");
	}

	// ═══════════════════════════════════════════════════════
	//  PATCH A — $Qt : Flick / Slide notes
	// ═══════════════════════════════════════════════════════
	/// <summary>
	/// Insert at index 0:
	///   ldarg.2; ldfld $md; call Math.Abs; ldc.r8 20; bgt.s ORIGINAL;
	///   ldarg.2; ldc.r8 0.0; stfld $md;
	///   ldc.i4.1; starg.s 3;
	///   // fall through to ORIGINAL
	/// </summary>
	static void PatchQt(TypeDef adClass, FieldDef fld_md, IMethod mathAbs)
	{
		var method = FindMethod(adClass, "$Qt");
		var body = method.Body;
		var il = body.Instructions;
		Console.WriteLine($"\n[PATCH A] {method.Name} — Flick/Slide auto-perfect");
		Console.WriteLine($"  Original IL count: {il.Count}");

		var originalFirst = il[0];

		// Build injection list (inserted before index 0)
		var inject = new[]
		{
			OpCodes.Ldarg_2.ToInstruction(),                       // load note ref
            new Instruction(OpCodes.Ldfld, fld_md),                // read $md
            new Instruction(OpCodes.Call, mathAbs),                 // Math.Abs($md)
            new Instruction(OpCodes.Ldc_R8, 20.0),                 // threshold 20ms
            new Instruction(OpCodes.Bgt_S, originalFirst),         // if >20 skip patch

            OpCodes.Ldarg_2.ToInstruction(),                       // load note ref
            new Instruction(OpCodes.Ldc_R8, 0.0),                  // 0.0
            new Instruction(OpCodes.Stfld, fld_md),                // $md = 0.0

            OpCodes.Ldc_I4_1.ToInstruction(),                      // true
            new Instruction(OpCodes.Starg_S, method.Parameters[3]),// inputDetected = true (param index 3)
        };

		for (int i = inject.Length - 1; i >= 0; i--)
			il.Insert(0, inject[i]);

		FixBranchTargets(body);
		Console.WriteLine($"  Patched IL count:  {il.Count}  (+{inject.Length})");
	}

	// ═══════════════════════════════════════════════════════
	//  PATCH B — $st (lowercase) : SkyArea / Follow notes
	// ═══════════════════════════════════════════════════════
	/// <summary>
	/// Modify specific instructions IN-PLACE to force $Vx and $vx to true:
	///   For $Vx: replace the ldloc.s (flag variable) right before stfld with ldc.i4.1
	///   For $vx: replace the two ldloc.s (flag4, flag5) before the 'and' with ldc.i4.1
	///            so that (true & true) = true → brtrue always takes the TRUE path
	/// IMPORTANT: Skip cleanup code where $Vx = false (ldc.i4.0 before stfld)
	/// </summary>
	static void PatchSt_lower(TypeDef adClass, FieldDef fld_Vx, FieldDef fld_vx)
	{
		var method = FindMethod(adClass, "$st");
		var body = method.Body;
		var il = body.Instructions;
		Console.WriteLine($"\n[PATCH B] {method.Name} — SkyArea/Follow auto-perfect");
		Console.WriteLine($"  Original IL count: {il.Count}");

		int patchCount = 0;
		for (int i = 0; i < il.Count; i++)
		{
			if (il[i].OpCode != OpCodes.Stfld) continue;
			var fld = il[i].Operand as IField;
			if (fld == null) continue;

			if (FieldMatches(fld, fld_Vx))
			{
				// Skip cleanup code: $Vx = false (ldc.i4.0 before stfld)
				if (i > 0 && il[i - 1].OpCode == OpCodes.Ldc_I4_0)
				{
					Console.WriteLine($"  [B] Skipped cleanup stfld $Vx at index {i} (constant false)");
					continue;
				}
				// Replace the ldloc.s (flag variable) right before stfld with ldc.i4.1 IN-PLACE
				if (i > 0 && (il[i - 1].OpCode == OpCodes.Ldloc_S || il[i - 1].OpCode == OpCodes.Ldloc))
				{
					il[i - 1].OpCode = OpCodes.Ldc_I4_1;
					il[i - 1].Operand = null;
					patchCount++;
					Console.WriteLine($"  [B] Forced $Vx = true at index {i} (replaced ldloc → ldc.i4.1)");
				}
			}
			else if (FieldMatches(fld, fld_vx))
			{
				// For $vx, scan backwards to find the 'and' instruction with two ldloc.s before it.
				// These load flag4 and flag5 which feed into (flag4 && flag5).
				// Replacing both with ldc.i4.1 makes the AND always true,
				// so brtrue always takes the TRUE path → $vx = true.
				bool found = false;
				for (int j = i - 1; j >= Math.Max(0, i - 40); j--)
				{
					if (il[j].OpCode == OpCodes.And &&
						j >= 2 &&
						(il[j - 1].OpCode == OpCodes.Ldloc_S || il[j - 1].OpCode == OpCodes.Ldloc) &&
						(il[j - 2].OpCode == OpCodes.Ldloc_S || il[j - 2].OpCode == OpCodes.Ldloc))
					{
						il[j - 2].OpCode = OpCodes.Ldc_I4_1;
						il[j - 2].Operand = null;
						il[j - 1].OpCode = OpCodes.Ldc_I4_1;
						il[j - 1].Operand = null;
						patchCount++;
						found = true;
						Console.WriteLine($"  [B] Forced $vx = true at index {i} (replaced 2x ldloc before 'and' → ldc.i4.1)");
						break;
					}
				}
				if (!found)
					Console.WriteLine($"  [B] WARNING: Could not find 'and' pattern for stfld $vx at index {i}");
			}
		}

		FixBranchTargets(body);
		Console.WriteLine($"  Patched IL count:  {il.Count}  (patched {patchCount} sites)");
	}

	// ═══════════════════════════════════════════════════════
	//  PATCH C — $St (uppercase) : Tap & Hold notes
	// ═══════════════════════════════════════════════════════
	/// <summary>
	/// 4 sub-patches:
	///   C1 — Auto-tap head (inject at top, after local init)
	///   C2 — Visual glow for hold (inject in for-loop)
	///   C3 — Bypass physical key check (replace ldfld $px → pop+ldc.i4.1)
	///   C4 — Hold tail perfect (force Math.Abs result → 0.0)
	///   C5 — Turn off glow when note ends ($px = false)
	/// </summary>
	static void PatchSt_upper(
		TypeDef adClass, ModuleDef module,
		FieldDef fld_md, FieldDef fld_pd, FieldDef fld_Pd,
		FieldDef fld_Id, FieldDef fld_px, FieldDef fld_fW,
		FieldDef fld_SW, FieldDef fld_od, IMethod mathAbs)
	{
		var method = FindMethod(adClass, "$St");
		var body = method.Body;
		var il = body.Instructions;
		Console.WriteLine($"\n[PATCH C] {method.Name} — Tap & Hold auto-perfect + visuals");
		Console.WriteLine($"  Original IL count: {il.Count}");

		// ── C1: Auto-tap head ──────────────────────────────
		// After "stloc.2" (num2 = -1), insert:
		//   ldarg.2; ldfld $md; call Math.Abs; ldc.r8 10; bgt.s SKIP;
		//   ldc.i4.1; stloc.1;          // flag = true
		//   ldarg.2; ldfld $pd; stloc.2 // num2 = note.$pd
		// SKIP: ...
		{
			int insertIdx = -1;
			// Find the pattern: ldc.i4.m1 ; stloc.2 (num2 = -1)
			for (int i = 0; i < il.Count - 1; i++)
			{
				if (il[i].OpCode == OpCodes.Ldc_I4_M1 && il[i + 1].OpCode == OpCodes.Stloc_2)
				{
					insertIdx = i + 2; // right after stloc.2
					break;
				}
			}
			if (insertIdx < 0) throw new Exception("[C1] Cannot find ldc.i4.m1 + stloc.2 pattern");

			var skipTarget = il[insertIdx]; // original next instruction

			var c1 = new[]
			{
				OpCodes.Ldarg_2.ToInstruction(),
				new Instruction(OpCodes.Ldfld, fld_md),
				new Instruction(OpCodes.Call, mathAbs),
				new Instruction(OpCodes.Ldc_R8, 10.0),
				new Instruction(OpCodes.Bgt_S, skipTarget),

				OpCodes.Ldc_I4_1.ToInstruction(),
				OpCodes.Stloc_1.ToInstruction(),

				OpCodes.Ldarg_2.ToInstruction(),
				new Instruction(OpCodes.Ldfld, fld_pd),
				OpCodes.Stloc_2.ToInstruction(),
			};

			for (int j = 0; j < c1.Length; j++)
				il.Insert(insertIdx + j, c1[j]);

			Console.WriteLine($"  [C1] Auto-tap head injected at index {insertIdx} (+{c1.Length})");
		}

		// ── C2: Visual glow ON for hold notes ──────────────
		// In the for-loop over lanes, find the first access to $fW[V_4].$Ox
		// and inject BEFORE it:
		//   ldarg.2; ldfld $Id; ldc.i4.2; bne.un.s SKIP;
		//   ldarg.2; ldfld $md; ldc.r8 0.0; bgt.un.s SKIP;
		//   ldarg.0; ldflda $SW; ldfld $fW; ldloc.s V_4; ldelema $ed;
		//   ldc.i4.1; stfld $px;
		// SKIP: ... (original $Ox access)
		{
			// Pattern: look for ldflda $SW ; ldfld $fW ; ldloc.s ; ldelema ; ldfld $Ox
			// inside the for-loop. The for-loop starts after the C1 injection area.
			// We search for the first occurrence of "ldfld $Ox" after $fW access that
			// follows our C1 injection.
			int fwIdx = -1;
			for (int i = 0; i < il.Count - 4; i++)
			{
				// Pattern: ldflda $SW ; ldfld $fW ; ldloc.s ; ldelema
				if (il[i].OpCode == OpCodes.Ldflda && FieldMatches(il[i].Operand as IField, fld_SW) &&
					il[i + 1].OpCode == OpCodes.Ldfld && FieldMatches(il[i + 1].Operand as IField, fld_fW) &&
					(il[i + 2].OpCode == OpCodes.Ldloc_S || il[i + 2].OpCode == OpCodes.Ldloc) &&
					il[i + 3].OpCode == OpCodes.Ldelema)
				{
					// Check if this is the first one in the for-loop (after C1 area)
					// The instruction before ldflda $SW should be ldarg.0
					if (i > 0 && il[i - 1].OpCode == OpCodes.Ldarg_0)
					{
						fwIdx = i - 1; // ldarg.0 position
						break;
					}
				}
			}
			if (fwIdx < 0) throw new Exception("[C2] Cannot find for-loop $fW access pattern");

			var skipTarget = il[fwIdx]; // the ldarg.0 before ldflda $SW

			// We need the local variable used for the loop counter (V_4)
			// It's the operand of ldloc.s at fwIdx+3
			var loopVar = (il[fwIdx + 3].Operand as Local) ?? throw new Exception("[C2] Cannot determine loop variable");

			// We also need reference to $ed type for ldelema
			var edTypeRef = (il[fwIdx + 4].Operand as ITypeDefOrRef) ?? throw new Exception("[C2] Cannot determine $ed type for ldelema");

			var c2 = new[]
			{
				OpCodes.Ldarg_2.ToInstruction(),
				new Instruction(OpCodes.Ldfld, fld_Id),
				new Instruction(OpCodes.Ldc_I4, NOTE_TYPE_HOLD),
				new Instruction(OpCodes.Bne_Un_S, skipTarget),

				OpCodes.Ldarg_2.ToInstruction(),
				new Instruction(OpCodes.Ldfld, fld_md),
				new Instruction(OpCodes.Ldc_R8, 0.0),
				new Instruction(OpCodes.Bgt_Un_S, skipTarget),

				OpCodes.Ldarg_0.ToInstruction(),
				new Instruction(OpCodes.Ldflda, fld_SW),
				new Instruction(OpCodes.Ldfld, fld_fW),
				new Instruction(OpCodes.Ldloc_S, loopVar),
				new Instruction(OpCodes.Ldelema, edTypeRef),
				OpCodes.Ldc_I4_1.ToInstruction(),
				new Instruction(OpCodes.Stfld, fld_px),
			};

			for (int j = 0; j < c2.Length; j++)
				il.Insert(fwIdx + j, c2[j]);

			// Fix the loop's back-edge: find the ble/blt that jumps back to the loop start
			// and redirect it to our new injected code start
			FixLoopBackEdge(il, fwIdx, c2.Length, loopVar);

			Console.WriteLine($"  [C2] Visual glow ON injected at index {fwIdx} (+{c2.Length})");
		}

		// ── C3: Bypass physical key check ──────────────────
		// Find: ldfld bool $px followed by brfalse
		// Replace the entire load chain + ldfld $px with nops + ldc.i4.1
		// IMPORTANT: We do NOT insert new instructions. Using il.Insert can corrupt
		//            nearby ldloc operands (V_4 → V_5 bug). All changes are in-place.
		// Pattern: ldarg.0; ldflda $SW; ldfld $fW; ldloc.s V; ldelema $ed; ldfld $px; brfalse
		// After:   nop; nop; nop; nop; nop; ldc.i4.1; brfalse (never branches since 1 != 0)
		{
			int patchCount = 0;
			for (int i = 0; i < il.Count - 1; i++)
			{
				if (il[i].OpCode == OpCodes.Ldfld &&
					FieldMatches(il[i].Operand as IField, fld_px) &&
					i + 1 < il.Count &&
					(il[i + 1].OpCode == OpCodes.Brfalse_S || il[i + 1].OpCode == OpCodes.Brfalse))
				{
					// Verify the load chain before ldfld $px:
					// Expected: ldarg.0; ldflda $SW; ldfld $fW; ldloc.s V; ldelema $ed; ldfld $px
					if (i >= 5 &&
						il[i - 5].OpCode == OpCodes.Ldarg_0 &&
						il[i - 4].OpCode == OpCodes.Ldflda && FieldMatches(il[i - 4].Operand as IField, fld_SW) &&
						il[i - 3].OpCode == OpCodes.Ldfld && FieldMatches(il[i - 3].Operand as IField, fld_fW) &&
						(il[i - 2].OpCode == OpCodes.Ldloc_S || il[i - 2].OpCode == OpCodes.Ldloc) &&
						il[i - 1].OpCode == OpCodes.Ldelema)
					{
						// Nop out the entire dead load chain (5 instructions)
						for (int j = i - 5; j < i; j++)
						{
							il[j].OpCode = OpCodes.Nop;
							il[j].Operand = null;
						}
						// Replace ldfld $px with ldc.i4.1 (always true)
						il[i].OpCode = OpCodes.Ldc_I4_1;
						il[i].Operand = null;
						// brfalse at i+1 stays unchanged — never branches since 1 != 0
						patchCount++;
						Console.WriteLine($"  [C3] Bypassed $px check at index {i} (nop chain + ldc.i4.1, no insert)");
					}
					else
					{
						// Fallback: nop out ldfld $px and brfalse (unconditional fall-through)
						// pop is needed to consume the value on stack before ldfld $px
						il[i].OpCode = OpCodes.Pop;
						il[i].Operand = null;
						il[i + 1].OpCode = OpCodes.Nop;
						il[i + 1].Operand = null;
						patchCount++;
						Console.WriteLine($"  [C3] Bypassed $px check at index {i} (fallback: pop + nop)");
					}
					break; // Only the first one in this method needs patching
				}
			}
			if (patchCount == 0)
				Console.WriteLine("  [C3] WARNING: No $px + brfalse pattern found to patch");
		}

		// ── C4: Hold tail perfect ──────────────────────────
		// Find the first call to Math.Abs AFTER the Tap head judgment section.
		// In the Hold tail section, the pattern is:
		//   ldloc.s V_9 (num3); call Math.Abs; stloc.s V_11 (num4)
		// We replace: call Math.Abs → pop + ldc.r8 0.0
		// 
		// Actually, from the diff, the change is simpler:
		//   Original: double num4 = Math.Abs(num3);
		//   Patched:  double num4 = 0.0;
		// In IL this means replacing the sequence that computes Math.Abs with just loading 0.0
		{
			// Find pattern: call Math.Abs(float64) followed by stloc.s
			// We need the FIRST one inside $St that corresponds to the Tap head section
			int absIdx = FindFirstMathAbsInTapSection(il, mathAbs);
			if (absIdx >= 0)
			{
				// Replace: load_value + call Math.Abs → ldc.r8 0.0
				// The instruction before call Math.Abs loads the value onto stack
				// We need to replace the value + Abs call with just 0.0
				// Actually from the diff: `ldloc.s V_9 ; call Math.Abs` becomes `ldc.r8 0.0`
				// Modify instructions IN-PLACE to preserve branch references
				if (absIdx > 0)
				{
					il[absIdx - 1].OpCode = OpCodes.Nop;
					il[absIdx - 1].Operand = null;
				}
				il[absIdx].OpCode = OpCodes.Ldc_R8;
				il[absIdx].Operand = 0.0;
				Console.WriteLine($"  [C4] Forced head tap time error to 0.0 at index {absIdx}");
			}
			else
			{
				Console.WriteLine("  [C4] WARNING: Math.Abs for tap head not found");
			}
		}

		// ── C5: Turn off glow when note finishes ───────────
		// Find: stfld bool $od (setting note as done in the hold-end section)
		// Insert after it: $SW.$fW[note.$pd].$px = false; $SW.$fW[note.$Pd].$px = false;
		{
			int odIdx = -1;
			// Search from the end of the method for `stfld $od` preceded by `ldc.i4.1`
			for (int i = il.Count - 1; i >= 0; i--)
			{
				if (il[i].OpCode == OpCodes.Stfld && FieldMatches(il[i].Operand as IField, fld_od) &&
					i > 0 && il[i - 1].OpCode == OpCodes.Ldc_I4_1)
				{
					odIdx = i;
					break;
				}
			}

			if (odIdx >= 0)
			{
				var edTypeRef = FindTypeDef(method.Module, NS_e, TYPE_ed);
				int insertAt = odIdx + 1;

				var c5 = new[]
				{
                    // $SW.$fW[note.$pd].$px = false
                    OpCodes.Ldarg_0.ToInstruction(),
					new Instruction(OpCodes.Ldflda, fld_SW),
					new Instruction(OpCodes.Ldfld, fld_fW),
					OpCodes.Ldarg_2.ToInstruction(),
					new Instruction(OpCodes.Ldfld, fld_pd),
					new Instruction(OpCodes.Ldelema, edTypeRef),
					OpCodes.Ldc_I4_0.ToInstruction(),
					new Instruction(OpCodes.Stfld, fld_px),

                    // $SW.$fW[note.$Pd].$px = false
                    OpCodes.Ldarg_0.ToInstruction(),
					new Instruction(OpCodes.Ldflda, fld_SW),
					new Instruction(OpCodes.Ldfld, fld_fW),
					OpCodes.Ldarg_2.ToInstruction(),
					new Instruction(OpCodes.Ldfld, fld_Pd),
					new Instruction(OpCodes.Ldelema, edTypeRef),
					OpCodes.Ldc_I4_0.ToInstruction(),
					new Instruction(OpCodes.Stfld, fld_px),
				};

				for (int j = 0; j < c5.Length; j++)
					il.Insert(insertAt + j, c5[j]);

				Console.WriteLine($"  [C5] Glow-off injected after stfld $od at index {insertAt} (+{c5.Length})");
			}
			else
			{
				Console.WriteLine("  [C5] WARNING: stfld $od not found for glow-off");
			}
		}

		FixBranchTargets(body);
		Console.WriteLine($"  Final IL count:    {il.Count}");
	}

	// ═══════════════════════════════════════════════════════
	//  Helper: find the first Math.Abs in the Tap section
	// ═══════════════════════════════════════════════════════
	static int FindFirstMathAbsInTapSection(IList<Instruction> il, IMethod mathAbs)
	{
		// In $St, the Tap head judgment uses Math.Abs(num3) → num4.
		// Pattern: after the note type check for $GD (ldc.i4.1) or $hD (ldc.i4.2)
		// with $FlA check, find the first call Math.Abs followed by stloc.s
		bool inTapSection = false;
		for (int i = 0; i < il.Count - 1; i++)
		{
			// Detect entry into tap/hold judgment section:
			// looking for: ldarg.2; ldfld $Id; ldc.i4.1; beq (this is the GD / tap note check)
			if (il[i].OpCode == OpCodes.Ldc_I4_1 &&
				i >= 2 &&
				il[i - 1].OpCode == OpCodes.Ldfld &&
				FieldNameMatches(il[i - 1].Operand as IField, FLD_Id) &&
				(il[i + 1].OpCode == OpCodes.Beq_S || il[i + 1].OpCode == OpCodes.Beq))
			{
				inTapSection = true;
			}

			if (inTapSection &&
				il[i].OpCode == OpCodes.Call &&
				IsMathAbs(il[i].Operand as IMethod) &&
				i + 1 < il.Count &&
				(il[i + 1].OpCode == OpCodes.Stloc_S || il[i + 1].OpCode == OpCodes.Stloc))
			{
				return i;
			}
		}
		return -1;
	}

	// ═══════════════════════════════════════════════════════
	//  Helper: find the existing Math.Abs(float64) reference in the game's IL
	// ═══════════════════════════════════════════════════════
	static IMethod? FindExistingMathAbs(TypeDef adClass)
	{
		// Search all methods in $ad for an existing call to System.Math::Abs(float64)
		foreach (var method in adClass.Methods)
		{
			if (method.Body == null) continue;
			foreach (var instr in method.Body.Instructions)
			{
				if (instr.OpCode == OpCodes.Call && instr.Operand is IMethod m &&
					IsMathAbs(m) &&
					m.MethodSig?.Params.Count == 1 &&
					m.MethodSig.Params[0].ElementType == dnlib.DotNet.ElementType.R8)
				{
					return m;
				}
			}
		}
		return null;
	}

	// ═══════════════════════════════════════════════════════
	//  Utility methods
	// ═══════════════════════════════════════════════════════

	static TypeDef FindTypeDef(ModuleDef module, string ns, string name)
	{
		// Try simple find first
		var result = module.Find($"{ns}.{name}", isReflectionName: true);
		if (result != null) return result;

		// Search nested types and all types
		foreach (var t in module.GetTypes())
		{
			if (t.Name == name && (t.Namespace == ns || t.DeclaringType?.Name == ns))
				return t;
		}
		// Search referenced assemblies
		foreach (var asmRef in module.GetAssemblyRefs())
		{
			var resolved = module.Context.AssemblyResolver.Resolve(asmRef, module);
			if (resolved == null) continue;
			foreach (var mod in resolved.Modules)
			{
				var found = mod.Find($"{ns}.{name}", isReflectionName: true);
				if (found != null) return found;
				foreach (var t in mod.GetTypes())
				{
					if (t.Name == name && (t.Namespace == ns || t.DeclaringType?.Name == ns))
						return t;
				}
			}
		}
		throw new Exception($"Type {ns}.{name} not found in module or references!");
	}

	static FieldDef FindField(TypeDef type, string name)
	{
		return type.Fields.FirstOrDefault(f => f.Name == name)
			?? throw new Exception($"Field {name} not found in {type.FullName}!");
	}

	static MethodDef FindMethod(TypeDef type, string name)
	{
		return type.Methods.FirstOrDefault(m => m.Name == name)
			?? throw new Exception($"Method {name} not found in {type.FullName}!");
	}

	static bool FieldMatches(IField? field, FieldDef target)
	{
		if (field == null) return false;
		return field.Name == target.Name &&
			   field.DeclaringType.FullName == target.DeclaringType.FullName;
	}

	static bool FieldNameMatches(IField? field, string name)
	{
		return field != null && field.Name == name;
	}

	static bool IsMathAbs(IMethod? method)
	{
		if (method == null) return false;
		return method.Name == "Abs" &&
			   (method.DeclaringType.FullName == "System.Math" ||
				method.DeclaringType.Name == "Math");
	}

	/// <summary>
	/// After inserting/removing instructions, short branch targets may need
	/// to be updated to long branches if offsets exceed sbyte range.
	/// dnlib handles offset calculation on write, but we call SimplifyBranches
	/// + OptimizeBranches to be safe.
	/// </summary>
	static void FixBranchTargets(CilBody body)
	{
		body.SimplifyBranches();
		body.OptimizeBranches();
		body.UpdateInstructionOffsets();
	}

	/// <summary>
	/// Fix the for-loop back-edge after C2 injection.
	/// The loop's ble/blt instruction at the bottom should jump back to our
	/// new injected code instead of the original loop body start.
	/// </summary>
	static void FixLoopBackEdge(IList<Instruction> il, int injectionStart, int injectedCount, Local loopVar)
	{
		// The back-edge is a ble/blt that:
		//   1) comes after the injection point
		//   2) jumps to a target that was the original loop body start (now at injectionStart + injectedCount)
		var originalBodyStart = il[injectionStart + injectedCount];

		for (int i = injectionStart + injectedCount; i < il.Count; i++)
		{
			if ((il[i].OpCode == OpCodes.Ble || il[i].OpCode == OpCodes.Ble_S ||
				 il[i].OpCode == OpCodes.Blt || il[i].OpCode == OpCodes.Blt_S ||
				 il[i].OpCode == OpCodes.Ble_Un || il[i].OpCode == OpCodes.Ble_Un_S) &&
				il[i].Operand is Instruction target &&
				target == originalBodyStart)
			{
				// Redirect to our injected code start
				il[i].Operand = il[injectionStart];
				Console.WriteLine($"  [C2] Fixed loop back-edge at index {i} → new target at {injectionStart}");
				return;
			}
		}
		Console.WriteLine("  [C2] WARNING: Could not find loop back-edge to fix");
	}
}
