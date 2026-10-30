# SmartHunter

支援《魔物獵人 世界/冰原》與《魔物獵人 荒野》的 Windows 資訊顯示工具。自動辨識遊戲，透過 overlay 顯示魔物血量、部位、狀態、隊伍傷害及玩家增益，不需要安裝遊戲模組。

兩款遊戲各自儲存設定、介面位置與翻譯。可調整字級、大小及配色，並支援資料同步、自動更新與荒野網頁觀戰。

## 安裝與使用

1. 使用 Windows x64，並安裝 [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)。
2. 取得 [release/](release/) 內的完整檔案，放在可寫入的資料夾，執行 `SmartHunter.exe`。
3. 啟動遊戲，建議使用無邊框或視窗模式。程式會自動讀取，執行狀態顯示在日誌中。

| 操作 | 方式 |
| --- | --- |
| 移動 overlay | 按住 `LeftAlt`，以滑鼠拖曳 |
| 縮放 overlay | 按住 `LeftAlt`，滾動滑鼠，每次調整 5% |
| 暫時隱藏 overlay | 按住 `F1` |
| 分享荒野觀戰 | 點選左側「啟用並複製觀戰連結」 |

設定變更會自動儲存。主視窗上方可選擇要修改的遊戲與語言；偵錯及網頁分享預設關閉。

## 舊版升級

關閉舊程式，將 `release/` 的完整檔案複製到原資料夾並替換程式檔。首次執行會自動轉換舊設定與翻譯，保留原有自訂內容；也可在「一般設定」多選匯入舊版 JSON。

兩款遊戲的資料分別儲存在 `Config/World`、`Config/Wilds` 與對應的 `Languages` 資料夾。自動更新預設從本專案 `unified` 分支的 `release/` 取得程式檔，保留個人設定。

升級完成後，已轉換的舊設定與確認不再使用的舊版檔案會移至 `LegacyFiles/日期時間/`，保留原始內容；確認不再需要後可自行刪除。仍在使用的自訂資源會保留原位。

## 建置

需要 Windows 與 .NET SDK 8。執行 `dotnet build SmartHunter.sln -c Release -p:Platform=x64`；本機發布檔案由 `tools/Package-Client.ps1` 產生。

## 致謝與來源

- [r00telement/SmartHunter](https://github.com/r00telement/SmartHunter) 與 [gabrielefilipp/SmartHunter](https://github.com/gabrielefilipp/SmartHunter)：原版功能與更新流程。
- [HunterPie](https://github.com/HunterPie/HunterPie)：荒野讀取與資料研究參考。
- Marcus101RR Cheat Table：最初四組荒野管理器特徵碼的研究來源；[作者討論串](https://opencheattables.com/viewtopic.php?t=1331)。
- 協助實機測試與翻譯的使用者。原版授權見 [LICENSE.txt](LICENSE.txt)。
