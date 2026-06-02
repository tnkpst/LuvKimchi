using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace LuvKimchi
{
    internal static class Program
    {
        [STAThread]
        static int Main()
        {
            ApplicationConfiguration.Initialize();
            try { Application.Run(new MainForm()); return 0; }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "LuvKimchi - Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }

    public class MainForm : Form
    {
        // UI
        private Button btnStart, btnStop, btnBind, btnReset;
        private Label lblBound, lblCountdown, lblDetail;
        private NumericUpDown numH, numM, numS;
        private CheckBox chkTop, chkAutoClose;
        private DateTimePicker dtClose;
        private Timer uiTimer;

        // State
        private bool running = false, binding = false;
        private DateTime nextTrigger;
        private TimeSpan interval = TimeSpan.Zero;
        private ushort boundVk = 0;         // no default key
        private ushort boundScan = 0;
        private string boundName = "—";     // shown until user binds
        private int count = 0;
        private DateTime? nextAutoClose = null;

        // Win32
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] static extern bool Beep(int f, int d);

        const int INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;
        const int  SW_MINIMIZE = 6;
        const uint ASFW_ANY = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public int type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        // Palette
        Color ColBg      = ColorTranslator.FromHtml("#E9F1FA");
        Color ColPanel   = Color.White;
        Color ColText    = ColorTranslator.FromHtml("#243447");
        Color ColAccent  = ColorTranslator.FromHtml("#8FB3D1");
        Color ColAccent2 = ColorTranslator.FromHtml("#7AA6C4");
        Color ColDisabledBg  = ColorTranslator.FromHtml("#EEF2F6");
        Color ColDisabledBorder = ColorTranslator.FromHtml("#CCD3DB");

        Font SafeFont(string name, float size, FontStyle style)
        {
            try { return new Font(name, size, style, GraphicsUnit.Point); }
            catch { return new Font("Segoe UI", size, style, GraphicsUnit.Point); }
        }

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            this.Text = "LuvKimchi by Nn.";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(860, 520);
            Size = new Size(900, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            TopMost = true; KeyPreview = true; BackColor = ColBg;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {}

            // fonts
            var f10 = SafeFont("Kanit", 10.5f, FontStyle.Regular);
            var f11 = SafeFont("Kanit", 11f, FontStyle.Regular);
            var f12 = SafeFont("Kanit", 12f, FontStyle.Regular);
            var f12b= SafeFont("Kanit", 12f, FontStyle.Bold);

            // root
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = ColBg, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); // header taller
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            // ===== Header
            var header = CreateCard(96, false);
            var hgrid = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = ColPanel, ColumnCount=2, RowCount=1, Padding = new Padding(12,10,12,10) };
            hgrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
            hgrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));

            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents=false, BackColor = ColPanel, Dock = DockStyle.Fill, Padding=new Padding(0,4,0,0)};
            btnBind = MakeButton("เลือกปุ่ม", f12b, true);
            btnBind.Width = 110; btnBind.Click += (_, __) => BeginBind();
            left.Controls.Add(btnBind);
            lblBound = new Label{ Text=$"ปุ่ม: {boundName}", Font=f12, ForeColor=ColAccent2, AutoSize=true, BackColor=ColPanel, Padding=new Padding(14,8,0,0)};
            left.Controls.Add(lblBound);
            hgrid.Controls.Add(left, 0, 0);

            // time row: number first then word
            var timeBox = new Panel { Dock = DockStyle.Fill, BackColor = ColPanel };
            int boxWidth = 420; int boxHeight = 42;
            var timeWrap = new Panel { BackColor = ColPanel, Width = boxWidth, Height = boxHeight, Anchor = AnchorStyles.Right };
            timeWrap.Location = new Point(timeBox.Width - boxWidth, (timeBox.Height - boxHeight)/2);
            timeBox.Resize += (_, __) => timeWrap.Location = new Point(timeBox.Width - boxWidth, (timeBox.Height - boxHeight)/2);

            var t = new FlowLayoutPanel{ Dock=DockStyle.None, AutoSize=true, BackColor=ColPanel, FlowDirection=FlowDirection.LeftToRight, WrapContents=false, Padding=new Padding(0)};
            numH = MakeNum(f12, 0, 23); t.Controls.Add(numH); t.Controls.Add(Word(" ชั่วโมง", f11));
            numM = MakeNum(f12, 0, 59); t.Controls.Add(numM); t.Controls.Add(Word(" นาที", f11));
            numS = MakeNum(f12, 0, 59); t.Controls.Add(numS); t.Controls.Add(Word(" วินาที", f11));
            timeWrap.Controls.Add(t);
            t.Location = new Point((timeWrap.Width - t.PreferredSize.Width)/2, (timeWrap.Height - t.PreferredSize.Height)/2);
            timeWrap.Resize += (_, __) => t.Location = new Point((timeWrap.Width - t.PreferredSize.Width)/2, (timeWrap.Height - t.PreferredSize.Height)/2);

            timeBox.Controls.Add(timeWrap);
            hgrid.Controls.Add(timeBox, 1, 0);
            header.Controls.Add(hgrid);
            root.Controls.Add(header, 0, 0);

            // ===== Buttons row
            var buttons = CreateCard(78, false);
            var br = new TableLayoutPanel{ Dock = DockStyle.Fill, BackColor=ColPanel, ColumnCount=3, RowCount=1, Padding = new Padding(10,8,10,8) };
            br.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            br.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            br.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            btnStart = MakeButton("กิมจิมั้ย?", f12b, true);
            btnStop  = MakeButton("ไม่กินอิ่มแล้ว", f12b, false); btnStop.Enabled=false;
            btnReset = MakeButton("ล้างค่า", f12b, true);
            btnStart.Click += (_, __) => StartMacro();
            btnStop.Click  += (_, __) => StopMacro();
            btnReset.Click += (_, __) => ResetAll();
            if(!btnStop.Enabled) ApplyDisabled(btnStop);
            br.Controls.AddRange(new Control[]{btnStart, btnStop, btnReset});
            buttons.Controls.Add(br);
            root.Controls.Add(buttons, 0, 1);

            // ===== Auto close
            var autoClose = CreateCard(64, false);
            var ac = new TableLayoutPanel{ Dock = DockStyle.Fill, BackColor=ColPanel, ColumnCount=4, RowCount=1, Padding = new Padding(10,8,10,8)};
            ac.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ac.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ac.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ac.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            chkAutoClose = new CheckBox{ Text="ปิดอัตโนมัติ เวลา", Font=f11, AutoSize=true, ForeColor=ColText, BackColor=ColPanel };
            dtClose = new DateTimePicker{ Format=DateTimePickerFormat.Custom, CustomFormat="HH:mm", ShowUpDown=true, Width=84, Font=f12 };
            var hint = new Label{ Text="(ถ้าเวลาที่ตั้งผ่านไปแล้ว จะนับเป็นวันถัดไปอัตโนมัติ)", Font=f10, ForeColor=Color.Gray, AutoSize=true, BackColor=ColPanel, Padding = new Padding(6,3,0,0)};
            chkTop = new CheckBox{ Text="ปักหมุด", Checked=true, Font=f11, AutoSize=true, ForeColor=ColAccent2, BackColor=ColPanel };
            chkTop.CheckedChanged += (_, __) => TopMost = chkTop.Checked;
            ac.Controls.Add(chkAutoClose,0,0); ac.Controls.Add(dtClose,1,0); ac.Controls.Add(hint,2,0); ac.Controls.Add(chkTop,3,0);
            autoClose.Controls.Add(ac);
            root.Controls.Add(autoClose, 0, 2);

            // ===== Status (always visible)
            var status = CreateCard(0, false);
            var st = new TableLayoutPanel{ Dock = DockStyle.Fill, BackColor=ColPanel, ColumnCount=1, RowCount=2, Padding = new Padding(10,8,10,8)};
            st.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            st.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lblCountdown = new Label{ Text="🥢 จะกินในอีก 00:00:00 • 🍲 0 ครั้ง", AutoSize=true, Font=f12, ForeColor=ColAccent2, BackColor=ColPanel, TextAlign=ContentAlignment.MiddleCenter, Dock = DockStyle.Top, Padding=new Padding(0,2,0,2)};
            lblDetail = new Label{ Text="(เลือกปุ่มก่อน • every 00:00:01)", AutoSize=true, Font=f11, ForeColor=ColText, BackColor=ColPanel, TextAlign=ContentAlignment.MiddleCenter, Dock = DockStyle.Top};
            st.Controls.Add(lblCountdown, 0, 0);
            st.Controls.Add(lblDetail, 0, 1);
            status.Controls.Add(st);
            root.Controls.Add(status, 0, 3);

            // events
            chkAutoClose.CheckedChanged += (_, __) => RecalcAutoClose();
            dtClose.ValueChanged += (_, __) => RecalcAutoClose();
            uiTimer = new Timer{ Interval = 1000 }; uiTimer.Tick += (_, __) => TickTimer();

            numH.Value = 0; numM.Value = 0; numS.Value = 0;
        }

        // helpers
        Panel CreateCard(int fixedHeight, bool grow = false) => new Panel{ Dock = grow ? DockStyle.Fill : DockStyle.Top, BackColor=ColPanel, Padding=new Padding(0), Margin=new Padding(0,0,0,8), BorderStyle=BorderStyle.FixedSingle, Height=fixedHeight };
        Button MakeButton(string text, Font f, bool primary)
        {
            var btn = new Button{ Text=text, Font=f, Height=40, Dock=DockStyle.Fill, FlatStyle=FlatStyle.Flat, Margin=new Padding(6) };
            if (primary){ btn.BackColor=ColAccent; btn.ForeColor=Color.White; btn.FlatAppearance.BorderSize=0; }
            else        { btn.BackColor=Color.White; btn.ForeColor=ColAccent; btn.FlatAppearance.BorderSize=1; btn.FlatAppearance.BorderColor=ColAccent; }
            btn.MouseEnter += (_, __)=> { btn.BackColor = primary ? ColAccent2 : ColDisabledBg; };
            btn.MouseLeave += (_, __)=> { btn.BackColor = primary ? ColAccent : Color.White; };
            return btn;
        }
        Label Word(string t, Font f) => new Label{ Text=t, Font=f, AutoSize=true, BackColor=ColPanel, ForeColor=ColText, Padding=new Padding(4,6,8,0)};
        NumericUpDown MakeNum(Font f, int def, int max) => new NumericUpDown{ Minimum=0, Maximum=max, Value=def, Width=64, Font=f, TextAlign=HorizontalAlignment.Center, BackColor=Color.White, ForeColor=ColText, Margin=new Padding(6,0,0,0)};
        void ApplyDisabled(Button b){ b.BackColor=ColDisabledBg; b.ForeColor=ColText; b.FlatAppearance.BorderSize=1; b.FlatAppearance.BorderColor=ColDisabledBorder; }

        void RecalcAutoClose()
        {
            if (!chkAutoClose.Checked){ nextAutoClose=null; return; }
            var now = DateTime.Now;
            var target = new DateTime(now.Year, now.Month, now.Day, dtClose.Value.Hour, dtClose.Value.Minute, 0);
            if (target <= now) target = target.AddDays(1);
            nextAutoClose = target;
        }

        void ResetAll()
        {
            running=false; uiTimer.Stop();
            btnStart.Enabled=true; btnStop.Enabled=false; ApplyDisabled(btnStop);
            count=0;
            numH.Value=0; numM.Value=0; numS.Value=0;
            boundVk=0; boundScan=0; boundName="—"; lblBound.Text="ปุ่ม: —";
            nextAutoClose=null; chkAutoClose.Checked=false;
            lblCountdown.Text="🥢 จะกินในอีก 00:00:00 • 🍲 0 ครั้ง";
            lblDetail.Text="(เลือกปุ่มก่อน • every 00:00:01)";
            Beep(750,80);
        }

        void BeginBind(){ binding=true; lblBound.Text="กดปุ่มที่ต้องการ…"; Focus(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(binding){
                boundVk=(ushort)e.KeyCode;
                boundScan=(ushort)MapVirtualKey(boundVk,0);
                boundName=e.KeyCode.ToString();
                lblBound.Text=$"ปุ่ม: {boundName}";
                binding=false; Beep(900,70); e.Handled=true;
            }
            base.OnKeyDown(e);
        }

        void StartMacro()
        {
            if(running) return;
            var h=(int)numH.Value; var m=(int)numM.Value; var s=(int)numS.Value;
            if(h==0 && m==0 && s==0) s=1;
            interval=new TimeSpan(h,m,s);
            running=true; FireOnce();
            nextTrigger=DateTime.Now+interval;
            btnStart.Enabled=false; btnStop.Enabled=true;
            uiTimer.Start(); TickTimer(); RecalcAutoClose();
        }
        void StopMacro(){ running=false; btnStart.Enabled=true; btnStop.Enabled=false; ApplyDisabled(btnStop); uiTimer.Stop(); }

        void TickTimer()
        {
            if(nextAutoClose.HasValue && DateTime.Now>=nextAutoClose.Value){ Close(); return; }
            var remain = running ? nextTrigger - DateTime.Now : TimeSpan.Zero;
            if(running && remain<=TimeSpan.Zero){ FireOnce(); nextTrigger=DateTime.Now+interval; remain=nextTrigger-DateTime.Now; }
            lblCountdown.Text=$"🥢 จะกินในอีก {remain.Hours:00}:{remain.Minutes:00}:{remain.Seconds:00} • 🍲 {count} ครั้ง";
            string every = $"{interval.Hours:00}:{interval.Minutes:00}:{interval.Seconds:00}";
            lblDetail.Text = (boundVk==0) ? $"(เลือกปุ่มก่อน • every {every})" : $"(Key {boundName} • every {every})";
        }

        void FireOnce()
        {
            if(boundVk==0) return; // not bound yet
            IntPtr target=IntPtr.Zero;
            try{
                var fivem=Process.GetProcesses().FirstOrDefault(p=>!string.IsNullOrEmpty(p.MainWindowTitle)&&p.MainWindowTitle.IndexOf("FiveM",StringComparison.OrdinalIgnoreCase)>=0);
                if(fivem!=null&&fivem.MainWindowHandle!=IntPtr.Zero) target=fivem.MainWindowHandle;
            }catch{}
            if(target==IntPtr.Zero){
                target=GetForegroundWindow();
                if(target==Handle||target==IntPtr.Zero){ ShowWindow(Handle, SW_MINIMIZE); Thread.Sleep(120); target=GetForegroundWindow(); }
            }
            if(target!=IntPtr.Zero){
                uint tgt=GetWindowThreadProcessId(target,out _), cur=GetCurrentThreadId();
                AllowSetForegroundWindow(ASFW_ANY); AttachThreadInput(cur,tgt,true); SetForegroundWindow(target); AttachThreadInput(cur,tgt,false); Thread.Sleep(24);
            }
            PressScan(boundScan); PressVk(boundVk);
            keybd_event((byte)boundVk,(byte)boundScan,0,UIntPtr.Zero); Thread.Sleep(5);
            keybd_event((byte)boundVk,(byte)boundScan,KEYEVENTF_KEYUP,UIntPtr.Zero);
            count++; Beep(860,90);
        }
        void PressScan(ushort scan){ if(scan==0) return; var a=new INPUT[2]; a[0].type=1; a[0].U.ki=new KEYBDINPUT{ wVk=0, wScan=scan, dwFlags=KEYEVENTF_SCANCODE }; a[1].type=1; a[1].U.ki=new KEYBDINPUT{ wVk=0, wScan=scan, dwFlags=KEYEVENTF_SCANCODE|KEYEVENTF_KEYUP }; SendInput(2,a,Marshal.SizeOf(typeof(INPUT))); Thread.Sleep(6); }
        void PressVk(ushort vk){ var a=new INPUT[2]; a[0].type=1; a[0].U.ki=new KEYBDINPUT{ wVk=vk, wScan=0, dwFlags=0 }; a[1].type=1; a[1].U.ki=new KEYBDINPUT{ wVk=vk, wScan=0, dwFlags=KEYEVENTF_KEYUP }; SendInput(2,a,Marshal.SizeOf(typeof(INPUT))); Thread.Sleep(6); }
    }
}
