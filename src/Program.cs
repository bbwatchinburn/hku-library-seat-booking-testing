using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace HkuLibrarySeatBooking
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, eventArgs) =>
            {
                MessageBox.Show(
                    "程序遇到未处理错误：\r\n" + eventArgs.Exception.Message,
                    "HKU 图书馆座位预约",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            string dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HKU-Library-Seat-Booking");
            string configPath = Path.Combine(dataDirectory, "settings.json");
            ConfigStore store = new ConfigStore(configPath);
            BookingConfig config = store.Load();
            bool firstRun = config == null || !config.SetupCompleted;
            if (firstRun)
            {
                config = BookingConfig.CreateDefault();
            }

            string password = store.ReadPassword(config);
            string cookie = store.ReadCookie(config);
            using (MainWindow window = new MainWindow(store, config, password, cookie, firstRun))
            {
                Application.Run(window);
            }
            return 0;
        }
    }

    internal sealed class MainWindow : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly ConfigStore store;
        private readonly Label statusTitle;
        private readonly Label headline;
        private readonly Label detail;
        private readonly Label target;
        private readonly Label syncHint;
        private readonly Button settingsButton;
        private readonly Button minimizeButton;
        private readonly Button closeButton;
        private readonly WinFormsTimer clockTimer;
        private readonly NotifyIcon trayIcon;
        private readonly BookingScheduler scheduler;
        private BookingConfig config;
        private string password;
        private string cookie;
        private StatusSnapshot lastStatus;
        private bool openingSettings;
        private bool exiting;
        private bool dragging;
        private Point dragStart;

        public MainWindow(
            ConfigStore store,
            BookingConfig config,
            string password,
            string cookie,
            bool firstRun)
        {
            this.store = store;
            this.config = config;
            this.password = password;
            this.cookie = cookie;
            this.Text = "HKU 图书馆座位预约";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(430, 248);
            this.BackColor = Color.FromArgb(18, 31, 45);
            this.ForeColor = Color.White;
            this.TopMost = true;
            this.ShowInTaskbar = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Font = new Font("Microsoft YaHei UI", 9F);

            statusTitle = new Label
            {
                Text = "HKU LIBRARIES · 自动预约",
                Location = new Point(24, 19),
                Size = new Size(300, 20),
                ForeColor = Color.FromArgb(138, 165, 186),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
            headline = new Label
            {
                Text = "准备启动",
                Location = new Point(20, 45),
                Size = new Size(360, 66),
                ForeColor = Color.FromArgb(236, 245, 242),
                Font = new Font("Segoe UI Semibold", 29F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            detail = new Label
            {
                Text = "正在读取本地设置",
                Location = new Point(24, 111),
                Size = new Size(380, 42),
                ForeColor = Color.FromArgb(172, 192, 207),
                Font = new Font("Microsoft YaHei UI", 9F),
                AutoEllipsis = true
            };
            target = new Label
            {
                Text = "目标：尚未安排",
                Location = new Point(24, 160),
                Size = new Size(382, 39),
                ForeColor = Color.FromArgb(203, 218, 226),
                Font = new Font("Microsoft YaHei UI", 9.2F),
                AutoEllipsis = true
            };
            syncHint = new Label
            {
                Text = "",
                Location = new Point(24, 201),
                Size = new Size(248, 20),
                ForeColor = Color.FromArgb(99, 177, 153),
                Font = new Font("Microsoft YaHei UI", 8F)
            };

            settingsButton = CreateActionButton("设置", 282, 198, 58);
            minimizeButton = CreateActionButton("−", 342, 198, 28);
            closeButton = CreateActionButton("×", 374, 198, 28);
            closeButton.Font = new Font("Segoe UI", 13F, FontStyle.Regular);
            settingsButton.Click += (sender, eventArgs) => this.OpenSettings();
            minimizeButton.Click += (sender, eventArgs) => this.HideToTray();
            closeButton.Click += (sender, eventArgs) => this.ExitApplication();

            this.Controls.Add(statusTitle);
            this.Controls.Add(headline);
            this.Controls.Add(detail);
            this.Controls.Add(target);
            this.Controls.Add(syncHint);
            this.Controls.Add(settingsButton);
            this.Controls.Add(minimizeButton);
            this.Controls.Add(closeButton);

            this.AttachDrag(statusTitle);
            this.AttachDrag(headline);
            this.AttachDrag(detail);
            this.AttachDrag(target);
            this.AttachDrag(syncHint);

            this.clockTimer = new WinFormsTimer();
            this.clockTimer.Interval = 200;
            this.clockTimer.Tick += (sender, eventArgs) => this.RefreshClock();

            this.trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "HKU 图书馆座位预约",
                Visible = true
            };
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示状态", null, (sender, eventArgs) => this.RestoreWindow());
            trayMenu.Items.Add("设置", null, (sender, eventArgs) => this.OpenSettings());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, (sender, eventArgs) => this.ExitApplication());
            this.trayIcon.ContextMenuStrip = trayMenu;
            this.trayIcon.DoubleClick += (sender, eventArgs) => this.RestoreWindow();

            this.scheduler = new BookingScheduler(this.OnStatusChanged);
            this.Resize += this.WindowResized;
            this.FormClosing += this.WindowClosing;
            this.Shown += (sender, eventArgs) =>
            {
                this.PlaceWindow();
                this.ApplyOpacity();
                if (firstRun)
                {
                    this.BeginInvoke(new Action(this.OpenSettings));
                }
                else
                {
                    this.StartScheduler();
                }
                this.clockTimer.Start();
            };
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            using (Pen border = new Pen(Color.FromArgb(45, 70, 86), 1))
            {
                eventArgs.Graphics.DrawRectangle(border, 0, 0, this.Width - 1, this.Height - 1);
            }
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(36, 185, 142)))
            {
                eventArgs.Graphics.FillRectangle(accent, 0, 0, 5, this.Height);
            }
        }

        private static Button CreateActionButton(string text, int x, int y, int width)
        {
            Button button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 47, 63),
                ForeColor = Color.FromArgb(214, 228, 234),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                TabStop = false
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(55, 80, 94);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 64, 80);
            return button;
        }

        private void StartScheduler()
        {
            if (!this.config.SetupCompleted)
            {
                return;
            }
            this.lastStatus = null;
            this.scheduler.Start(this.config, this.password, this.cookie);
        }

        private void OnStatusChanged(StatusSnapshot status)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }
            this.BeginInvoke(new Action(() =>
            {
                this.lastStatus = status;
                this.statusTitle.Text = FriendlyState.Title(status.State).ToUpperInvariant();
                this.headline.Text = status.Headline;
                this.headline.ForeColor = status.State == RunState.Success
                    ? Color.FromArgb(92, 224, 175)
                    : status.State == RunState.Failed || status.State == RunState.NeedsAttention
                        ? Color.FromArgb(255, 176, 125)
                        : Color.FromArgb(236, 245, 242);
                this.detail.Text = status.Detail;
                if (status.Attempt != null)
                {
                    this.UpdateTarget(status.Attempt);
                }
                this.RefreshClock();
            }));
        }

        private void RefreshClock()
        {
            if (this.config == null || !this.config.SetupCompleted)
            {
                this.headline.Text = "需要设置";
                this.detail.Text = "请录入账号、Cookie 和预约目标。";
                return;
            }

            BookingAttempt attempt = ReleaseSchedule.Plan(this.config, DateTime.Now);
            this.UpdateTarget(attempt);
            if (this.lastStatus == null)
            {
                this.headline.Text = ReleaseSchedule.FormatCountdown(attempt.ReleaseAt - DateTime.Now);
                return;
            }

            if (this.lastStatus.State == RunState.Waiting)
            {
                TimeSpan remaining = attempt.ReleaseAt - DateTime.Now;
                this.headline.Text = remaining > TimeSpan.Zero
                    ? ReleaseSchedule.FormatCountdown(remaining)
                    : "00:00:00";
                this.statusTitle.Text = "等待放号";
                this.detail.Text = "放号 " + attempt.ReleaseAt.ToString("MM-dd HH:mm:ss") +
                    " · 到点自动提交";
            }
            else if (this.lastStatus.State == RunState.Preparing &&
                     attempt.ReleaseAt - DateTime.Now > TimeSpan.Zero)
            {
                this.headline.Text = ReleaseSchedule.FormatCountdown(attempt.ReleaseAt - DateTime.Now);
                this.statusTitle.Text = "正在准备 · 倒计时";
            }
        }

        private void UpdateTarget(BookingAttempt attempt)
        {
            this.target.Text = "目标  " + ReleaseSchedule.FormatTarget(
                attempt,
                this.config.AreaName,
                this.config.SeatLabel);
            if (attempt != null)
            {
                this.syncHint.Text = "放号 " + attempt.ReleaseAt.ToString("MM-dd HH:mm:ss") +
                    " · 重试 " + this.config.MaxRetries + " 次";
            }
        }

        private void OpenSettings()
        {
            if (this.openingSettings)
            {
                return;
            }
            if (!this.Visible)
            {
                this.RestoreWindow();
            }
            this.openingSettings = true;
            bool wasRunning = this.scheduler.IsRunning;
            this.scheduler.Stop();
            try
            {
                using (SettingsForm dialog = new SettingsForm(
                    this.store,
                    this.config,
                    this.password,
                    this.cookie,
                    !this.config.SetupCompleted))
                {
                    DialogResult result = dialog.ShowDialog(this);
                    if (result == DialogResult.OK && dialog.SavedConfig != null)
                    {
                        this.config = dialog.SavedConfig;
                        this.password = this.store.ReadPassword(this.config);
                        this.cookie = this.store.ReadCookie(this.config);
                        this.ApplyOpacity();
                        this.StartScheduler();
                    }
                    else if (wasRunning)
                    {
                        this.StartScheduler();
                    }
                    else if (!this.config.SetupCompleted)
                    {
                        this.ExitApplication();
                    }
                }
            }
            finally
            {
                this.openingSettings = false;
            }
        }

        private void ApplyOpacity()
        {
            this.Opacity = Math.Max(0.35, Math.Min(1.0, this.config.OpacityPercent / 100.0));
        }

        private void PlaceWindow()
        {
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (this.config.WindowLeft.HasValue && this.config.WindowTop.HasValue)
            {
                Point saved = new Point(
                    (int)this.config.WindowLeft.Value,
                    (int)this.config.WindowTop.Value);
                if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(
                    new Rectangle(saved, this.Size))))
                {
                    this.Location = saved;
                    return;
                }
            }

            this.Location = new Point(
                workArea.Right - this.Width - 24,
                workArea.Bottom - this.Height - 24);
        }

        private void AttachDrag(Control control)
        {
            control.MouseDown += this.BeginDrag;
            control.MouseMove += this.DragMove;
            control.MouseUp += this.EndDrag;
        }

        private void BeginDrag(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Left)
            {
                return;
            }
            this.dragging = true;
            this.dragStart = eventArgs.Location;
            ReleaseCapture();
            SendMessage(this.Handle, WmNclButtonDown, HtCaption, 0);
        }

        private void DragMove(object sender, MouseEventArgs eventArgs)
        {
            if (!this.dragging)
            {
                return;
            }
        }

        private void EndDrag(object sender, MouseEventArgs eventArgs)
        {
            if (!this.dragging)
            {
                return;
            }
            this.dragging = false;
            this.config.WindowLeft = this.Left;
            this.config.WindowTop = this.Top;
            try
            {
                this.store.Save(this.config);
            }
            catch
            {
            }
        }

        private void WindowResized(object sender, EventArgs eventArgs)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.HideToTray();
            }
        }

        private void HideToTray()
        {
            this.WindowState = FormWindowState.Normal;
            this.Hide();
            this.ShowInTaskbar = false;
        }

        private void RestoreWindow()
        {
            this.ShowInTaskbar = true;
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            this.BringToFront();
        }

        private void ExitApplication()
        {
            this.exiting = true;
            this.scheduler.Stop();
            this.trayIcon.Visible = false;
            this.clockTimer.Stop();
            this.Close();
        }

        private void WindowClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (!this.exiting)
            {
                this.exiting = true;
                this.scheduler.Stop();
                this.trayIcon.Visible = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.clockTimer.Dispose();
                this.trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
