# In_Falsus_auto_play

這是一個針對特定 In Falsus 的自動遊玩 (Auto-Play) 補丁工具。
它會修改遊戲的 `Game.dll`，將核心判定方法替換為自動完美判定 (Auto-Perfect)。

## 使用方法

1. 從 [Releases](../../releases) 頁面下載最新版本的壓縮檔並解壓縮。
2. 找到你遊戲目錄下的 `Game.dll` 檔案 (通常位於 `<Your Game dir>\if-app_Data\Managed\Game.dll`)。
3. 將 `Game.dll` 複製並貼上到本工具 (`In_Falsus_auto_play.exe`) 所在的同一個資料夾中。
4. 雙擊執行 `In_Falsus_auto_play.exe`。
5. 程式執行完畢後，會在同一個資料夾下生成一個名為 `Game_patched.dll` 的檔案。
6. 將生成的 `Game_patched.dll` 重新命名為 `Game.dll`。
7. 將這個新的 `Game.dll` 覆蓋回遊戲原本的 `Managed` 資料夾中 (建議先備份原本的 `Game.dll`)。

## 注意事項

- 本工具僅供學習與研究使用。
- 使用前請**務必備份**你原本的 `Game.dll` 檔案，以免發生意外無法恢復。
- 遊戲更新後，需要重新執行此工具或等待工具更新。