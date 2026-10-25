# 假力：滑鼠模式，Caps Lock LED表示功能爲了REALFORCE（真力）RC1

這是一款 Windows 應用程式，可使用 Topre REALFORCE RC1 鍵盤的類比按鍵控制滑鼠和Caps Lock LED表示。程式會讀取按鍵的按壓深度，並將其轉換為游標移動、點擊與捲動。

> **僅支援 Windows：** 此版本需要 Windows 和 .NET 8 SDK。使用程式時，REALFORCE 鍵盤必須持續連接。關閉程式或拔除鍵盤後，滑鼠控制與 LED 效果都會停止。（我自己是 Linux/Mac 使用者，也不喜歡 Microsoft 產品，但我必須遵從民主。我未來會加入 Mac 和 Linux 支援。）

> **專案說明：** 本專案以 vibe coding 方式製作，並使用 REA 逆向分析原始軟體與韌體以了解其運作。功能、效能與使用者介面尚未經過完整驗證；這是實驗性軟體，可能不完整或不穩定。請勿用於重要用途。

## 專案緣由

Topre 做出了很厲害的鍵盤。我第一次接觸 Topre 軸體，是在日本 7-Eleven ATM 的數字鍵盤上；據說那裡使用的也是 REALFORCE 同款軸體。這段經驗後來讓我買了 REALFORCE RC1，而我果然非常喜歡它。用這把鍵盤打字，連日常工作都像在演戲：問 AI「JSON 是誰？」時，感覺像蘇格拉底向柏拉圖追問思想；寫客戶郵件時，彷彿西塞羅在寫信給元老院；在 Teams 上向 PM 抱怨期限時，又像是三島由紀夫。對我來說，這把鍵盤就像達文西與米開朗基羅共同創作的作品。

但 RC1 有個令人遺憾的缺點。Topre 的設定軟體在 R4、GX1 等部分型號上提供滑鼠模式與 Caps Lock LED 指示功能，卻沒有提供給 RC1。根據我的逆向分析，RC1 硬體似乎能驅動這些狀態 LED 效果，只是官方軟體沒有開放這些功能。我感到很失望，就像電影教父，麥可發現自己的兄弟弗雷多背叛了他。

抱著「有志者事竟成」的想法，在 GPT-5.6 Luna 的協助下，我使用 REA 研究原始軟體與韌體中的 LED 控制流程。這項研究促成了 FaKeFORCE：一款為 RC1 加入滑鼠模式與 Caps Lock LED 效果的實驗性 companion app。

## 功能

- W/A/S/D：依類比按鍵的按壓深度移動游標。
- Q/E：滑鼠左鍵／右鍵。
- J/K：向下／向上捲動。
- Fn + Right-Shift：開啟或關閉滑鼠模式。
- 滑鼠模式啟用時，狀態 LED 會循環顯示 RGB 顏色。Caps Lock 的紅色閃爍優先顯示。這些效果是暫時的，不會修改鍵盤韌體或已儲存的燈光設定。
- Caps Lock LED 效果會依照 Windows 的 Caps Lock 開啟狀態運作，而不只是按下按鍵時觸發。Caps Lock 開啟期間，LED 會以設定的顏色與閃爍時間閃爍，並取代滑鼠模式的 LED 效果。可在 **LED Effects** 中調整顏色與時間。停用 LED 效果後，兩種效果都會停止。使用此功能時，程式必須保持執行且鍵盤必須持續連接。
- 設定畫面可調整速度、死區、Power 或 S 曲線、按鍵配置、啟動快捷鍵與 LED 效果。

## 安裝與啟動

1. 從 Microsoft 安裝 **[Windows 版 .NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)**。安裝完成後，請開啟新的 PowerShell 視窗。
2. 連接 REALFORCE RC1 鍵盤。
3. 在存放庫資料夾開啟 PowerShell，執行：

   ```powershell
   cd .\src
   dotnet run
   ```

第一次執行時，程式會先建置，再於背景啟動，並加入 Windows 使用者登入時的自動啟動項目。時鐘附近的通知區域會出現程式圖示。命令返回後可以關閉 PowerShell，程式仍會繼續執行。

按下 **Fn + Right-Shift** 開啟滑鼠模式，再按一次即可關閉。滑鼠模式啟用時，指定的移動、點擊與捲動按鍵會被攔截，不會作為一般文字輸入傳送至其他應用程式。

## 開啟設定

當系統匣程式正在執行時，在存放庫的 `src` 資料夾開啟 PowerShell 並執行：

```powershell
dotnet run --no-build
```

這會要求目前執行中的程式開啟設定視窗，不會再啟動第二個滑鼠控制器。也可以按右鍵點選系統匣圖示，再選擇 **Mouse sensitivity...**。

- **Mouse Mode：** 顯示啟動快捷鍵的說明。
- **Sensitivity：** 調整移動速度、死區、曲線形狀與曲線強度。Power 曲線會由慢逐漸加速；S 曲線則會在起步與結尾較平緩，在中段反應較強。
- **Key Assignment：** 設定移動、點擊、捲動與啟動功能使用的類比插槽及鍵盤按鍵。
- **LED Effects：** 啟用或停用 LED 效果、選擇顏色與時間，並預覽效果。

變更會立即套用至正在執行的程式。按下 **Save** 可保留設定供下次啟動使用。設定會儲存在 `%APPDATA%\RealforceApcMouse\settings.json`。

## 結束程式或停用自動啟動

按右鍵點選系統匣圖示並選擇 **Quit**，即可結束程式。這不會移除 Windows 登入時的自動啟動項目。

若要停用自動啟動，請在程式執行期間於 `src` 資料夾開啟 PowerShell，並執行：

```powershell
dotnet run --no-build -- uninstall
```

此命令不會結束目前執行中的程式；請另外從系統匣選擇 **Quit**。再次執行 `dotnet run` 會重新啟動程式並重新啟用自動啟動。

## 建置與疑難排解

若只要建置而不啟動程式，請執行：

```powershell
cd .\src
dotnet build
```

若出現 `realforce-apc-mouse.exe` 被鎖定的錯誤，表示背景程式仍在執行。請從系統匣選擇 **Quit**，再執行 `dotnet run`。Windows 自動啟動項目會指向此資料夾中的執行檔；首次啟動後請勿移動存放庫資料夾。

若找不到鍵盤，請確認鍵盤已連接，再從 `src` 執行 `dotnet run -- list`。`dotnet run -- scan` 可協助確認類比插槽編號；`dotnet run -- status 17 --watch` 可監看指定插槽（請將 `17` 換成要檢查的插槽編號）。診斷命令可按 Ctrl+C 結束。

`dotnet run -- led-test` 是選用的硬體診斷功能。確認後，程式會暫時寫入 LED 設定區塊並還原原值。若不想測試鍵盤已儲存的燈光設定，請勿執行。

協定與技術細節請參閱 [docs/PROTOCOL.md](docs/PROTOCOL.md) 和 [docs/LED_TRACE.md](docs/LED_TRACE.md)。
