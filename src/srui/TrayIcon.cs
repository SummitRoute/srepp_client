////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

namespace srui
{
    public partial class TrayIcon : Form
    {
        private string szVersion = "0.0.0.1";

        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.NotifyIcon TrayNotifyIcon;
        private System.Windows.Forms.ContextMenuStrip TrayContextMenuStrip;
        private System.Windows.Forms.Button BtnClose;
        private System.Windows.Forms.ToolStripMenuItem SettingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem HelpToolStripMenuItem;
        private System.Windows.Forms.Label LblAbout;

        public TrayIcon()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TrayIcon));
            this.TrayNotifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
            
            // Context menu strip
            this.TrayContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.SettingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();

            this.TrayContextMenuStrip.SuspendLayout();
            this.SuspendLayout();

            // 
            // Set up tray icon
            //
            this.TrayNotifyIcon.Icon = ((System.Drawing.Icon)(resources.GetObject("taskbar.ico")));
            this.TrayNotifyIcon.Text = "Summit Route End-Point Protection";
            this.TrayNotifyIcon.Visible = true;
            // Attach menu strip
            this.TrayNotifyIcon.ContextMenuStrip = this.TrayContextMenuStrip;
            this.TrayNotifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.TrayNotifyIcon_MouseDoubleClick);

            // 
            // Tool Strip Menu Items
            // 
            // Settings
            this.SettingsToolStripMenuItem.Name = "SettingsToolStripMenuItem";
            this.SettingsToolStripMenuItem.Text = "Settings...";
            this.SettingsToolStripMenuItem.Click += new System.EventHandler(this.SettingsToolStripMenuItem_Click);

            // Help
            this.HelpToolStripMenuItem.Name = "HelpToolStripMenuItem";
            this.HelpToolStripMenuItem.Text = "Help";
            this.HelpToolStripMenuItem.Click += new System.EventHandler(this.HelpToolStripMenuItem_Click);


            // 
            // TrayContextMenuStrip
            // 
            this.TrayContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.SettingsToolStripMenuItem,
                this.HelpToolStripMenuItem,
            });
            this.TrayContextMenuStrip.Name = "TrayContextMenuStrip";
            this.TrayContextMenuStrip.Size = new System.Drawing.Size(115, 70);

            InitializeComponent();

            // Make sure nothing shows except the tray icon initially
            this.TrayContextMenuStrip.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
            this.HideSettingsForm();
        }

        protected override void Dispose(bool disposing)
        {
            // Clean up any components being used.
            if (disposing && components != null)
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void HideSettingsForm()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false; // Remove from taskbar
            base.Hide();
        }

        private void ShowSettingsForm()
        {
            SetFormValues();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Show();
            this.ResumeLayout(true);
        }

        /// <summary>
        ///  Tray icon double click opens settings window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TrayNotifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ShowSettingsForm();
        }

        private void SettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TrayContextMenuStrip.Hide();
            ShowSettingsForm();
        }

        private void HelpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TrayContextMenuStrip.Hide();
            Process.Start(String.Format("https://app.summitroute.com/help?version={0}",szVersion));
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TrayIcon));
            this.SuspendLayout();

            int formWidth = 500;
            int formHeight = 300;
            int margin = 15;

            // 
            // BtnClose
            //
            this.BtnClose = new System.Windows.Forms.Button();
            this.BtnClose.Text = "Close";
            this.BtnClose.Width = 150;
            this.BtnClose.Location = new System.Drawing.Point(formWidth - this.BtnClose.Size.Width - margin, formHeight - this.BtnClose.Size.Height * 2 - margin);
            this.BtnClose.TabIndex = 0;
            this.BtnClose.Click += new System.EventHandler(this.CloseBtn_Click);

            //
            // LblAbout
            //
            this.LblAbout = new System.Windows.Forms.Label();
            this.LblAbout.Text = "This version does not have any settings to display.";
            this.LblAbout.Width = formWidth;
            this.LblAbout.Location = new System.Drawing.Point(margin, margin);

            // 
            // TrayIcon Settings Form
            // 
            this.ClientSize = new System.Drawing.Size(formWidth, formHeight);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("icon.ico")));
            this.ControlBox = false;
            this.Controls.Add(this.BtnClose);
            this.Controls.Add(this.LblAbout);
            
            this.MinimumSize = new System.Drawing.Size(formWidth, formHeight);
            this.MaximumSize = new System.Drawing.Size(formWidth, formHeight);
            this.Name = "TrayIcon";
            this.Text = "Summit Route EPP v"+szVersion+" Settings";
            this.WindowState = System.Windows.Forms.FormWindowState.Minimized;
            this.ResumeLayout(false);

        }

        private void CloseBtn_Click(object sender, EventArgs e)
        {
            //Application.Exit();
            HideSettingsForm();
        }

        private void SaveCloseBtn_Click(object sender, EventArgs e)
        {
            // TODO Save
        }

        private void SetFormValues()
        {
            // TODO Set form
        }

        public void DisplayNotification(string msg)
        {
            this.TrayNotifyIcon.BalloonTipTitle = "Summit Route EPP";
            this.TrayNotifyIcon.BalloonTipText = msg;
            this.TrayNotifyIcon.BalloonTipIcon = ToolTipIcon.Error;
            this.TrayNotifyIcon.Visible = true;
            this.TrayNotifyIcon.ShowBalloonTip(3000);
        }
    }
}
