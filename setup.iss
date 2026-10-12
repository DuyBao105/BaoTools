[Setup]
AppName=BaoTools
AppVersion=v10.5
AppPublisher=Châu Tinh Trì
AppPublisherURL=https://discord.com/channels/1543559756504113152
AppSupportURL=https://discord.com/channels/1543559756504113152
AppUpdatesURL=https://discord.com/channels/1543559756504113152
DefaultDirName={autopf}\BaoTools
DefaultGroupName=BaoTools
OutputDir=out_setup
OutputBaseFilename=BaoTools_v10.5_Setup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=src\BaoToolsGui\icon.ico
UninstallDisplayIcon={app}\BaoTools.exe

; Modern Gaming UI Style
WizardStyle=modern
WizardResizable=no
WizardSizePercent=120,120
WizardImageFile=assets\installer_banner.bmp
WizardSmallImageFile=assets\installer_small.bmp

; Streamline UX (Welcome -> Select Dir -> Install -> Complete)
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no

[Languages]
Name: "vi"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppTitle=BaoTools Gaming Suite
SetupWindowTitle=Cài đặt BaoTools - Gaming Launcher v10.5
WelcomeLabel1=CHÀO MỪNG ĐẾN VỚI BAOTOOLS!
WelcomeLabel2=Bộ công cụ quản lý game, tối ưu hóa Steam và tự động chẩn đoán sửa lỗi PE hàng đầu thế hệ mới.%n%nNhấn 'Cài đặt ngay' để bắt đầu thiết lập, hoặc 'Hủy' để thoát.
SelectDirLabel3=Thư mục cài đặt BaoTools:
SelectDirBrowseLabel=Để thay đổi thư mục lưu trữ, nhấn 'Duyệt tìm'...
InstallingLabel=Hệ thống đang giải nén tệp tin và thiết lập môi trường chơi game tối ưu...
FinishedHeadingLabel=CÀI ĐẶT HOÀN TẤT THÀNH CÔNG!
FinishedLabel=BaoTools v10.5 đã sẵn sàng hỗ trợ các tựa game yêu thích của bạn.%n%nNhấn 'Khởi chạy ngay' để trải nghiệm launcher ngay bây giờ.
ClickFinish=Nhấn 'Khởi chạy ngay' để bắt đầu.

[Files]
Source: "out_portable\BaoTools.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "out_portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\BaoTools"; Filename: "{app}\BaoTools.exe"
Name: "{autodesktop}\BaoTools"; Filename: "{app}\BaoTools.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình Desktop (Khuyến nghị)"; GroupDescription: "Tùy chọn bổ sung:"

[Run]
Filename: "{app}\BaoTools.exe"; Description: "Khởi chạy BaoTools ngay bây giờ"; Flags: nowait postinstall skipifsilent

[Code]
// Gaming Dark Theme - Delphi BGR color mapping
const
  COLOR_BG_DARK     = $140D0A; // Deep Midnight Navy / Obsidian (#0A0D14)
  COLOR_PANEL_TOP   = $1E1610; // Slate Card Header (#10161E)
  COLOR_EDIT_BG     = $241B13; // Dark Input Box (#131B24)
  COLOR_TEXT_WHITE  = $FFFFFF; // Crisp White
  COLOR_TEXT_MUTED  = $A8A098; // Subtle Slate Gray (#98A0A8)
  COLOR_CYAN_ACCENT = $F8BD38; // Neon Cyan (#38BDF8 in BGR)

procedure InitializeWizard;
begin
  // Set main form dark background and modern font
  WizardForm.Color := COLOR_BG_DARK;
  WizardForm.Font.Name := 'Segoe UI';

  // Remove vintage beveled lines
  WizardForm.Bevel.Visible := False;
  WizardForm.BeveledLabel.Visible := False;

  // Welcome Page (Dark theme & styling)
  WizardForm.WelcomePage.Color := COLOR_BG_DARK;
  WizardForm.WelcomeLabel1.Font.Name := 'Segoe UI';
  WizardForm.WelcomeLabel1.Font.Size := 14;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.WelcomeLabel1.Font.Color := COLOR_TEXT_WHITE;

  WizardForm.WelcomeLabel2.Font.Name := 'Segoe UI';
  WizardForm.WelcomeLabel2.Font.Size := 9;
  WizardForm.WelcomeLabel2.Font.Color := $D0C8C0;

  // Top header panel for inner pages
  WizardForm.MainPanel.Color := COLOR_PANEL_TOP;
  WizardForm.PageNameLabel.Font.Name := 'Segoe UI';
  WizardForm.PageNameLabel.Font.Size := 11;
  WizardForm.PageNameLabel.Font.Style := [fsBold];
  WizardForm.PageNameLabel.Font.Color := COLOR_TEXT_WHITE;

  WizardForm.PageDescriptionLabel.Font.Name := 'Segoe UI';
  WizardForm.PageDescriptionLabel.Font.Size := 9;
  WizardForm.PageDescriptionLabel.Font.Color := COLOR_TEXT_MUTED;

  // Inner pages styling
  WizardForm.InnerPage.Color := COLOR_BG_DARK;

  // Select Destination Directory Page
  WizardForm.SelectDirLabel.Font.Name := 'Segoe UI';
  WizardForm.SelectDirLabel.Font.Color := COLOR_TEXT_WHITE;
  WizardForm.SelectDirBrowseLabel.Font.Name := 'Segoe UI';
  WizardForm.SelectDirBrowseLabel.Font.Color := COLOR_TEXT_MUTED;

  WizardForm.DirEdit.Color := COLOR_EDIT_BG;
  WizardForm.DirEdit.Font.Name := 'Segoe UI';
  WizardForm.DirEdit.Font.Color := COLOR_TEXT_WHITE;
  WizardForm.DirEdit.BorderStyle := bsSingle;

  // Tasks Page
  WizardForm.TasksList.Color := COLOR_EDIT_BG;
  WizardForm.TasksList.Font.Name := 'Segoe UI';
  WizardForm.TasksList.Font.Color := COLOR_TEXT_WHITE;
  WizardForm.TasksList.BorderStyle := bsSingle;

  // Installing Page
  WizardForm.StatusLabel.Font.Name := 'Segoe UI';
  WizardForm.StatusLabel.Font.Color := COLOR_TEXT_WHITE;
  WizardForm.FileNameLabel.Font.Name := 'Segoe UI';
  WizardForm.FileNameLabel.Font.Color := COLOR_CYAN_ACCENT;

  // Finished Page
  WizardForm.FinishedPage.Color := COLOR_BG_DARK;
  WizardForm.FinishedHeadingLabel.Font.Name := 'Segoe UI';
  WizardForm.FinishedHeadingLabel.Font.Size := 14;
  WizardForm.FinishedHeadingLabel.Font.Style := [fsBold];
  WizardForm.FinishedHeadingLabel.Font.Color := COLOR_TEXT_WHITE;

  WizardForm.FinishedLabel.Font.Name := 'Segoe UI';
  WizardForm.FinishedLabel.Font.Size := 9;
  WizardForm.FinishedLabel.Font.Color := $D0C8C0;

  // Navigation Buttons
  WizardForm.NextButton.Font.Name := 'Segoe UI';
  WizardForm.NextButton.Font.Style := [fsBold];
  WizardForm.BackButton.Font.Name := 'Segoe UI';
  WizardForm.CancelButton.Font.Name := 'Segoe UI';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Dynamic Game-Style Button Captions
  if CurPageID = wpWelcome then
  begin
    WizardForm.NextButton.Caption := 'Cài đặt ngay >';
  end
  else if CurPageID = wpFinished then
  begin
    WizardForm.NextButton.Caption := 'Khởi chạy ngay';
  end
  else
  begin
    WizardForm.NextButton.Caption := 'Tiếp tục >';
  end;
end;
