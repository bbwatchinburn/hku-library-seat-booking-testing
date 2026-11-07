using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HkuLibrarySeatBooking
{
    internal sealed class SettingsForm : Form
    {
        private readonly ConfigStore store;
        private readonly BookingConfig original;
        private readonly bool firstRun;
        private readonly ComboBox areaCombo;
        private readonly TextBox areaUrlBox;
        private readonly TextBox usernameBox;
        private readonly TextBox passwordBox;
        private readonly TextBox cookieBox;
        private readonly CheckBox cookieOnlyBox;
        private readonly TextBox seatBox;
        private readonly ComboBox slotCombo;
        private readonly TextBox customStartBox;
        private readonly TextBox customEndBox;
        private readonly DateTimePicker releaseTimePicker;
        private readonly NumericUpDown targetOffsetBox;
        private readonly NumericUpDown retryCountBox;
        private readonly NumericUpDown retryIntervalBox;
        private readonly NumericUpDown opacityBox;
        private readonly TextBox loginUrlBox;
        private readonly TextBox loginUserFieldBox;
        private readonly TextBox loginPassFieldBox;
        private readonly TextBox seatFieldNameBox;
        private readonly TextBox seatFieldValueBox;
        private readonly TextBox slotFieldNameBox;
        private readonly TextBox slotFieldValueBox;
        private readonly TextBox submitFieldNameBox;
        private readonly TextBox submitFieldValueBox;
        private readonly CheckBox showSecretsBox;

        public SettingsForm(ConfigStore store, BookingConfig config, string password, string cookie, bool firstRun)
        {
            this.store = store;
            this.original = config.Clone();
            this.firstRun = firstRun;

            this.Text = firstRun ? "首次设置 · HKU 图书馆座位预约" : "设置 · HKU 图书馆座位预约";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(610, 700);
            this.BackColor = Color.FromArgb(246, 247, 249);
            this.Font = new Font("Microsoft YaHei UI", 9F);

            Label title = new Label
            {
                Text = firstRun ? "录入港大预约信息" : "修改预约设置",
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 42, 66),
                AutoSize = true,
                Location = new Point(24, 18)
            };
            Label subtitle = new Label
            {
                Text = "敏感信息仅保存在本机，并使用当前 Windows 用户的 DPAPI 加密。",
                ForeColor = Color.FromArgb(92, 103, 117),
                AutoSize = true,
                Location = new Point(26, 49)
            };
            this.Controls.Add(title);
            this.Controls.Add(subtitle);

            TabControl tabs = new TabControl
            {
                Location = new Point(24, 76),
                Size = new Size(562, 557),
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            this.Controls.Add(tabs);

            TabPage basic = new TabPage("预约设置");
            basic.BackColor = Color.White;
            basic.AutoScroll = true;
            basic.AutoScrollMinSize = new Size(0, 625);
            TabPage advanced = new TabPage("高级适配");
            advanced.BackColor = Color.White;
            tabs.TabPages.Add(basic);
            tabs.TabPages.Add(advanced);

            int y = 16;
            AddSection(basic, "登录与鉴权", ref y);
            this.areaCombo = AddCombo(basic, "目标馆区", AreaPresets.Names().ToArray(), ref y, false);
            this.areaCombo.SelectedIndexChanged += this.AreaChanged;
            this.areaUrlBox = AddTextBox(basic, "馆区 URL", ref y, false);
            this.usernameBox = AddTextBox(basic, "HKU 用户名", ref y, false);
            this.passwordBox = AddSecretBox(basic, "密码", ref y);
            this.cookieBox = AddSecretBox(basic, "浏览器 Cookie", ref y);
            this.cookieBox.Multiline = true;
            this.cookieBox.Height = 54;
            y += 18;
            this.cookieOnlyBox = AddCheckBox(basic, "仅使用 Cookie（推荐，兼容 HKU 单点登录）", ref y);
            this.showSecretsBox = AddCheckBox(basic, "临时显示密码和 Cookie", ref y);
            this.showSecretsBox.CheckedChanged += this.ToggleSecrets;

            AddSection(basic, "座位与时段", ref y);
            this.seatBox = AddTextBox(basic, "目标座位（留空=自动匹配）", ref y, false);
            this.slotCombo = AddCombo(basic, "预约时段", BookingSlots.Names, ref y, true);
            this.slotCombo.SelectedIndexChanged += this.SlotChanged;
            Panel customPanel = new Panel
            {
                Location = new Point(150, y),
                Size = new Size(345, 30)
            };
            this.customStartBox = new TextBox
            {
                Location = new Point(0, 0),
                Width = 74,
                Text = config.CustomStartTime
            };
            this.customEndBox = new TextBox
            {
                Location = new Point(104, 0),
                Width = 74,
                Text = config.CustomEndTime
            };
            customPanel.Controls.Add(this.customStartBox);
            customPanel.Controls.Add(new Label { Text = "至", Location = new Point(82, 4), AutoSize = true });
            customPanel.Controls.Add(this.customEndBox);
            basic.Controls.Add(customPanel);
            y += 42;

            AddSection(basic, "放号与重试", ref y);
            this.releaseTimePicker = AddTimePicker(basic, "每天放号时间", ref y);
            this.targetOffsetBox = AddNumber(basic, "目标日期（放号后第几天）", 0, 7, ref y);
            this.retryCountBox = AddNumber(basic, "失败自动重试次数", 0, 50, ref y);
            this.retryIntervalBox = AddNumber(basic, "重试间隔（毫秒）", 100, 60000, 100, ref y);
            this.opacityBox = AddNumber(basic, "窗口透明度（%）", 35, 100, ref y);

            int advancedY = 16;
            Label advancedHelp = new Label
            {
                Text = "通常留空。只有港大网页字段变化、自动识别失效时才需要填写。",
                ForeColor = Color.FromArgb(92, 103, 117),
                Location = new Point(16, advancedY),
                Size = new Size(510, 38)
            };
            advanced.Controls.Add(advancedHelp);
            advancedY += 44;
            this.loginUrlBox = AddAdvancedTextBox(advanced, "登录页 URL", ref advancedY);
            this.loginUserFieldBox = AddAdvancedTextBox(advanced, "用户名字段名", ref advancedY);
            this.loginPassFieldBox = AddAdvancedTextBox(advanced, "密码字段名", ref advancedY);
            this.seatFieldNameBox = AddAdvancedTextBox(advanced, "座位字段名", ref advancedY);
            this.seatFieldValueBox = AddAdvancedTextBox(advanced, "座位字段值", ref advancedY);
            this.slotFieldNameBox = AddAdvancedTextBox(advanced, "时段字段名", ref advancedY);
            this.slotFieldValueBox = AddAdvancedTextBox(advanced, "时段字段值", ref advancedY);
            this.submitFieldNameBox = AddAdvancedTextBox(advanced, "提交按钮字段名", ref advancedY);
            this.submitFieldValueBox = AddAdvancedTextBox(advanced, "提交按钮值", ref advancedY);

            Button cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(414, 650),
                Size = new Size(80, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(42, 54, 68)
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(205, 212, 220);
            Button save = new Button
            {
                Text = "保存并开始监听",
                Location = new Point(504, 650),
                Size = new Size(82, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(20, 113, 92),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
            save.FlatAppearance.BorderSize = 0;
            save.Click += this.SaveClicked;
            this.Controls.Add(cancel);
            this.Controls.Add(save);
            this.CancelButton = cancel;

            this.Populate(config, password, cookie);
        }

        public BookingConfig SavedConfig { get; private set; }

        private void Populate(BookingConfig config, string password, string cookie)
        {
            this.areaCombo.Text = config.AreaName;
            this.areaUrlBox.Text = config.BookingPageUrl;
            this.usernameBox.Text = config.Username ?? string.Empty;
            this.passwordBox.Text = password ?? string.Empty;
            this.cookieBox.Text = cookie ?? string.Empty;
            this.cookieOnlyBox.Checked = config.UseCookieOnly;
            this.seatBox.Text = config.SeatLabel ?? string.Empty;
            this.slotCombo.SelectedIndex = Math.Max(0, Math.Min(config.TimeSlotIndex, 3));
            this.customStartBox.Text = config.CustomStartTime;
            this.customEndBox.Text = config.CustomEndTime;

            TimeSpan release;
            if (!TimeSpan.TryParseExact(config.ReleaseTime, "hh\\:mm", System.Globalization.CultureInfo.InvariantCulture, out release))
            {
                release = TimeSpan.Zero;
            }
            DateTime today = DateTime.Today.Add(release);
            this.releaseTimePicker.Value = today;
            this.targetOffsetBox.Value = config.TargetDayOffset;
            this.retryCountBox.Value = config.MaxRetries;
            this.retryIntervalBox.Value = config.RetryIntervalMs;
            this.opacityBox.Value = config.OpacityPercent;
            this.loginUrlBox.Text = config.LoginUrl ?? string.Empty;
            this.loginUserFieldBox.Text = config.LoginUsernameField ?? string.Empty;
            this.loginPassFieldBox.Text = config.LoginPasswordField ?? string.Empty;
            this.seatFieldNameBox.Text = config.SeatFieldName ?? string.Empty;
            this.seatFieldValueBox.Text = config.SeatFieldValue ?? string.Empty;
            this.slotFieldNameBox.Text = config.SlotFieldName ?? string.Empty;
            this.slotFieldValueBox.Text = config.SlotFieldValue ?? string.Empty;
            this.submitFieldNameBox.Text = config.SubmitFieldName ?? string.Empty;
            this.submitFieldValueBox.Text = config.SubmitFieldValue ?? string.Empty;
            this.SlotChanged(this, EventArgs.Empty);
        }

        private void AreaChanged(object sender, EventArgs e)
        {
            AreaPreset preset = AreaPresets.Find(this.areaCombo.Text);
            if (preset != null)
            {
                this.areaUrlBox.Text = preset.Url;
            }
        }

        private void SlotChanged(object sender, EventArgs e)
        {
            bool custom = this.slotCombo.SelectedIndex == 3;
            this.customStartBox.Enabled = custom;
            this.customEndBox.Enabled = custom;
        }

        private void ToggleSecrets(object sender, EventArgs e)
        {
            char mask = this.showSecretsBox.Checked ? '\0' : '●';
            this.passwordBox.PasswordChar = mask;
            this.cookieBox.PasswordChar = mask;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            try
            {
                BookingConfig config = this.original.Clone();
                config.AreaName = this.areaCombo.Text.Trim();
                config.BookingPageUrl = this.areaUrlBox.Text.Trim();
                config.Username = this.usernameBox.Text.Trim();
                config.SeatLabel = this.seatBox.Text.Trim();
                config.TimeSlotIndex = this.slotCombo.SelectedIndex;
                config.CustomStartTime = this.customStartBox.Text.Trim();
                config.CustomEndTime = this.customEndBox.Text.Trim();
                config.ReleaseTime = this.releaseTimePicker.Value.ToString("HH:mm");
                config.TargetDayOffset = (int)this.targetOffsetBox.Value;
                config.MaxRetries = (int)this.retryCountBox.Value;
                config.RetryIntervalMs = (int)this.retryIntervalBox.Value;
                config.OpacityPercent = (int)this.opacityBox.Value;
                config.UseCookieOnly = this.cookieOnlyBox.Checked;
                config.LoginUrl = this.loginUrlBox.Text.Trim();
                config.LoginUsernameField = this.loginUserFieldBox.Text.Trim();
                config.LoginPasswordField = this.loginPassFieldBox.Text.Trim();
                config.SeatFieldName = this.seatFieldNameBox.Text.Trim();
                config.SeatFieldValue = this.seatFieldValueBox.Text.Trim();
                config.SlotFieldName = this.slotFieldNameBox.Text.Trim();
                config.SlotFieldValue = this.slotFieldValueBox.Text.Trim();
                config.SubmitFieldName = this.submitFieldNameBox.Text.Trim();
                config.SubmitFieldValue = this.submitFieldValueBox.Text.Trim();

                Uri areaUri;
                if (!Uri.TryCreate(config.BookingPageUrl, UriKind.Absolute, out areaUri) ||
                    (areaUri.Scheme != Uri.UriSchemeHttps && areaUri.Scheme != Uri.UriSchemeHttp))
                {
                    throw new FormatException("馆区 URL 必须是完整的 http(s) 地址。");
                }
                if (config.UseCookieOnly && string.IsNullOrWhiteSpace(this.cookieBox.Text))
                {
                    throw new FormatException("请粘贴已登录港大预约系统后的 Cookie。");
                }
                if (!config.UseCookieOnly &&
                    (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrEmpty(this.passwordBox.Text)))
                {
                    throw new FormatException("请填写 HKU 用户名和密码，或切换为 Cookie 模式。");
                }
                if (config.TimeSlotIndex == 3)
                {
                    BookingSlots.Create("自定义时段", config.CustomStartTime, config.CustomEndTime);
                }

                config.SetupCompleted = true;
                this.store.SetCookie(config, this.cookieBox.Text.Trim());
                this.store.SetPassword(config, this.passwordBox.Text);
                this.store.Save(config);
                this.SavedConfig = config;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void AddSection(Control parent, string text, ref int y)
        {
            Label label = new Label
            {
                Text = text,
                Location = new Point(18, y),
                Size = new Size(510, 24),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 113, 92)
            };
            parent.Controls.Add(label);
            y += 30;
        }

        private static TextBox AddTextBox(Control parent, string label, ref int y, bool readOnly)
        {
            AddLabel(parent, label, y);
            TextBox box = new TextBox
            {
                Location = new Point(150, y - 3),
                Size = new Size(345, 25),
                ReadOnly = readOnly
            };
            parent.Controls.Add(box);
            y += 32;
            return box;
        }

        private static TextBox AddSecretBox(Control parent, string label, ref int y)
        {
            TextBox box = AddTextBox(parent, label, ref y, false);
            box.PasswordChar = '●';
            return box;
        }

        private static TextBox AddAdvancedTextBox(Control parent, string label, ref int y)
        {
            AddLabel(parent, label, y, 150);
            TextBox box = new TextBox
            {
                Location = new Point(172, y - 3),
                Size = new Size(350, 25)
            };
            parent.Controls.Add(box);
            y += 36;
            return box;
        }

        private static ComboBox AddCombo(Control parent, string label, string[] values, ref int y, bool dropDownList)
        {
            AddLabel(parent, label, y);
            ComboBox combo = new ComboBox
            {
                Location = new Point(150, y - 3),
                Size = new Size(345, 25),
                DropDownStyle = dropDownList ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown
            };
            combo.Items.AddRange(values);
            parent.Controls.Add(combo);
            y += 32;
            return combo;
        }

        private static CheckBox AddCheckBox(Control parent, string label, ref int y)
        {
            CheckBox box = new CheckBox
            {
                Text = label,
                Location = new Point(150, y - 2),
                Size = new Size(345, 24),
                AutoSize = true,
                Checked = true
            };
            parent.Controls.Add(box);
            y += 30;
            return box;
        }

        private static DateTimePicker AddTimePicker(Control parent, string label, ref int y)
        {
            AddLabel(parent, label, y);
            DateTimePicker picker = new DateTimePicker
            {
                Location = new Point(150, y - 3),
                Size = new Size(110, 25),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true
            };
            parent.Controls.Add(picker);
            y += 32;
            return picker;
        }

        private static NumericUpDown AddNumber(
            Control parent,
            string label,
            int minimum,
            int maximum,
            ref int y)
        {
            return AddNumber(parent, label, minimum, maximum, 1, ref y);
        }

        private static NumericUpDown AddNumber(
            Control parent,
            string label,
            int minimum,
            int maximum,
            int increment,
            ref int y)
        {
            AddLabel(parent, label, y);
            NumericUpDown number = new NumericUpDown
            {
                Location = new Point(150, y - 3),
                Size = new Size(110, 25),
                Minimum = minimum,
                Maximum = maximum,
                Increment = increment
            };
            parent.Controls.Add(number);
            y += 32;
            return number;
        }

        private static void AddLabel(Control parent, string text, int y)
        {
            AddLabel(parent, text, y, 116);
        }

        private static void AddLabel(Control parent, string text, int y, int width)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                Location = new Point(18, y),
                Size = new Size(width, 21),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(54, 64, 77)
            });
        }
    }
}
