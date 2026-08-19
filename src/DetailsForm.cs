using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ComNotify
{
    /// <summary>Full table of every COM port Windows knows about, connected or not.</summary>
    internal class DetailsForm : Form
    {
        private static DetailsForm instance;

        private readonly ListView list = new ListView();
        private readonly Label status = new Label();
        private readonly Func<List<PortInfo>> refresh;

        public static void ShowSingleton(Func<List<PortInfo>> refresh)
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new DetailsForm(refresh);
                instance.Show();
            }
            else
            {
                instance.Reload();
                if (instance.WindowState == FormWindowState.Minimized)
                    instance.WindowState = FormWindowState.Normal;
                instance.Activate();
            }
            Native.SetForegroundWindow(instance.Handle);
        }

        public static void ReloadIfOpen()
        {
            if (instance != null && !instance.IsDisposed) instance.Reload();
        }

        private DetailsForm(Func<List<PortInfo>> refresh)
        {
            this.refresh = refresh;

            Text = "ComNotify — serial ports";
            Size = new Size(Ui.Px(1000), Ui.Px(460));
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Ui.Px(620), Ui.Px(260));
            Font = new Font("Segoe UI", 9f);
            try { Icon = IconFactory.Create(Ui.Px(32), true, 0); } catch { }

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.MultiSelect = true;
            list.HideSelection = false;
            list.Columns.Add("Port", Ui.Px(70));
            list.Columns.Add("Status", Ui.Px(100));
            list.Columns.Add("Bus", Ui.Px(80));
            list.Columns.Add("Description", Ui.Px(230));
            list.Columns.Add("Vendor", Ui.Px(140));
            list.Columns.Add("VID / PID", Ui.Px(130));
            list.Columns.Add("Serial", Ui.Px(120));
            list.Columns.Add("Device instance ID", Ui.Px(320));
            list.DoubleClick += delegate { CopySelection(); };
            list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.C) { CopySelection(); e.Handled = true; }
                if (e.KeyCode == Keys.F5) { Reload(); e.Handled = true; }
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            };

            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Copy port name", null, delegate { CopyColumn(0); });
            cm.Items.Add("Copy whole row", null, delegate { CopySelection(); });
            cm.Items.Add("Copy device instance ID", null, delegate { CopyColumn(7); });
            list.ContextMenuStrip = cm;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = Ui.Px(44);

            int btnW = Ui.Px(92);
            int btnH = Ui.Px(26);
            int top = Ui.Px(8);

            Button refreshBtn = new Button();
            refreshBtn.Text = "&Refresh";
            refreshBtn.Size = new Size(btnW, btnH);
            refreshBtn.Location = new Point(Ui.Px(10), top);
            refreshBtn.Click += delegate { Reload(); };

            Button copyAll = new Button();
            copyAll.Text = "Copy &all";
            copyAll.Size = new Size(btnW, btnH);
            copyAll.Location = new Point(Ui.Px(10) + btnW + Ui.Px(8), top);
            copyAll.Click += delegate { CopyAll(); };

            Button close = new Button();
            close.Text = "&Close";
            close.Size = new Size(btnW, btnH);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            close.Click += delegate { Close(); };

            status.AutoSize = false;
            status.Location = new Point(Ui.Px(10) + 2 * (btnW + Ui.Px(8)) + Ui.Px(6), Ui.Px(12));
            status.Size = new Size(Ui.Px(420), Ui.Px(20));
            status.ForeColor = SystemColors.GrayText;

            bottom.Controls.Add(refreshBtn);
            bottom.Controls.Add(copyAll);
            bottom.Controls.Add(status);
            bottom.Controls.Add(close);
            Controls.Add(list);
            Controls.Add(bottom);
            close.Location = new Point(bottom.ClientSize.Width - btnW - Ui.Px(10), top);
            CancelButton = close;

            Reload();
        }

        public void Reload()
        {
            List<PortInfo> ports = refresh();
            list.BeginUpdate();
            list.Items.Clear();
            int connected = 0, usb = 0;
            foreach (PortInfo p in ports)
            {
                if (p.Present) connected++;
                if (p.IsUsb) usb++;
                ListViewItem it = new ListViewItem(p.PortName);
                it.SubItems.Add(p.Present ? "Connected" : "Not connected");
                it.SubItems.Add(p.BusLabel);
                it.SubItems.Add(p.DisplayName);
                it.SubItems.Add(p.VendorLabel);
                it.SubItems.Add(p.VidPid);
                it.SubItems.Add(p.SerialNumber);
                it.SubItems.Add(p.InstanceId);
                it.ForeColor = p.Present ? SystemColors.WindowText : SystemColors.GrayText;
                if (p.Present && p.IsUsb) it.Font = new Font(list.Font, FontStyle.Bold);
                list.Items.Add(it);
            }
            list.EndUpdate();
            status.Text = string.Format("{0} port(s) known · {1} connected · {2} USB", ports.Count, connected, usb);
        }

        private void CopyColumn(int index)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ListViewItem it in list.SelectedItems)
                if (index < it.SubItems.Count) sb.AppendLine(it.SubItems[index].Text);
            SetClipboard(sb.ToString().TrimEnd());
        }

        private void CopySelection()
        {
            StringBuilder sb = new StringBuilder();
            foreach (ListViewItem it in list.SelectedItems)
            {
                List<string> cells = new List<string>();
                foreach (ListViewItem.ListViewSubItem si in it.SubItems) cells.Add(si.Text);
                sb.AppendLine(string.Join("\t", cells.ToArray()));
            }
            SetClipboard(sb.ToString().TrimEnd());
        }

        private void CopyAll()
        {
            StringBuilder sb = new StringBuilder();
            List<string> headers = new List<string>();
            foreach (ColumnHeader c in list.Columns) headers.Add(c.Text);
            sb.AppendLine(string.Join("\t", headers.ToArray()));
            foreach (ListViewItem it in list.Items)
            {
                List<string> cells = new List<string>();
                foreach (ListViewItem.ListViewSubItem si in it.SubItems) cells.Add(si.Text);
                sb.AppendLine(string.Join("\t", cells.ToArray()));
            }
            SetClipboard(sb.ToString().TrimEnd());
            status.Text = "Copied " + list.Items.Count + " row(s) to the clipboard.";
        }

        private static void SetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { }
        }
    }
}
