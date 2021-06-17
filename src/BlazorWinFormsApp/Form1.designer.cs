using System;

namespace BlazorWinFormsApp
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.blazorWebView1 = new PeakSWC.RemoteBlazorWebView.WindowsForms.BlazorWebView();
            this.linkLabel1 = new System.Windows.Forms.LinkLabel();
            this.SuspendLayout();
            // 
            // blazorWebView1
            // 
            this.blazorWebView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.blazorWebView1.Location = new System.Drawing.Point(0, 0);
            this.blazorWebView1.Margin = new System.Windows.Forms.Padding(0);
            this.blazorWebView1.Name = "blazorWebView1";
            this.blazorWebView1.Size = new System.Drawing.Size(1008, 729);
            this.blazorWebView1.TabIndex = 20;
            // 
            // linkLabel1
            // 
            this.linkLabel1.AutoSize = true;
            this.linkLabel1.Location = new System.Drawing.Point(64, 28);
            this.linkLabel1.Name = "linkLabel1";
            this.linkLabel1.Size = new System.Drawing.Size(60, 15);
            this.linkLabel1.TabIndex = 21;
            this.linkLabel1.TabStop = true;
            this.linkLabel1.Text = "linkLabel1";
            this.linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LinkLabel1_LinkClicked);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1008, 729);
            this.Controls.Add(this.linkLabel1);
            this.Controls.Add(this.blazorWebView1);
            this.Margin = new System.Windows.Forms.Padding(2, 1, 2, 1);
            this.Name = "Form1";
            this.Text = "Blazor Web in Windows Forms";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private PeakSWC.RemoteBlazorWebView.WindowsForms.BlazorWebView blazorWebView1;
        private System.Windows.Forms.LinkLabel linkLabel1;

        //  this.blazorWebView1.ServerUri = new System.Uri("https://localhost:443", System.UriKind.Absolute);
        //   this.blazorWebView1.Id = new System.Guid("00000000-0000-0000-0000-000000000000");
    }
}
