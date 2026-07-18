namespace Portly.Chatbox
{
    partial class ServerPanel
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
            label1 = new Label();
            UsersListBox = new ListBox();
            BtnKickUser = new Button();
            BtnChangeChannel = new Button();
            label2 = new Label();
            CmbChannels = new ComboBox();
            label3 = new Label();
            BtnConnectNewUser = new Button();
            label4 = new Label();
            ChatHistoryListBox = new ListBox();
            label5 = new Label();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 12F);
            label1.Location = new Point(12, 40);
            label1.Name = "label1";
            label1.Size = new Size(128, 21);
            label1.TabIndex = 0;
            label1.Text = "Connected users:";
            // 
            // UsersListBox
            // 
            UsersListBox.FormattingEnabled = true;
            UsersListBox.Location = new Point(12, 88);
            UsersListBox.Name = "UsersListBox";
            UsersListBox.Size = new Size(226, 319);
            UsersListBox.TabIndex = 1;
            // 
            // BtnKickUser
            // 
            BtnKickUser.Location = new Point(244, 88);
            BtnKickUser.Name = "BtnKickUser";
            BtnKickUser.Size = new Size(157, 30);
            BtnKickUser.TabIndex = 2;
            BtnKickUser.Text = "Kick user";
            BtnKickUser.UseVisualStyleBackColor = true;
            // 
            // BtnChangeChannel
            // 
            BtnChangeChannel.Location = new Point(244, 119);
            BtnChangeChannel.Name = "BtnChangeChannel";
            BtnChangeChannel.Size = new Size(157, 30);
            BtnChangeChannel.TabIndex = 3;
            BtnChangeChannel.Text = "Change user channel";
            BtnChangeChannel.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Segoe UI", 12F);
            label2.Location = new Point(244, 60);
            label2.Name = "label2";
            label2.Size = new Size(157, 21);
            label2.TabIndex = 4;
            label2.Text = "Available commands:";
            // 
            // CmbChannels
            // 
            CmbChannels.FormattingEnabled = true;
            CmbChannels.Location = new Point(88, 62);
            CmbChannels.Name = "CmbChannels";
            CmbChannels.Size = new Size(150, 23);
            CmbChannels.TabIndex = 5;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 12F);
            label3.Location = new Point(12, 64);
            label3.Name = "label3";
            label3.Size = new Size(70, 21);
            label3.TabIndex = 6;
            label3.Text = "Channel:";
            // 
            // BtnConnectNewUser
            // 
            BtnConnectNewUser.Location = new Point(12, 410);
            BtnConnectNewUser.Name = "BtnConnectNewUser";
            BtnConnectNewUser.Size = new Size(226, 30);
            BtnConnectNewUser.TabIndex = 7;
            BtnConnectNewUser.Text = "Connect new user";
            BtnConnectNewUser.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            label4.Location = new Point(193, 9);
            label4.Name = "label4";
            label4.Size = new Size(235, 30);
            label4.TabIndex = 8;
            label4.Text = "Chatbox Server Panel";
            // 
            // ChatHistoryListBox
            // 
            ChatHistoryListBox.FormattingEnabled = true;
            ChatHistoryListBox.Location = new Point(244, 181);
            ChatHistoryListBox.Name = "ChatHistoryListBox";
            ChatHistoryListBox.Size = new Size(357, 259);
            ChatHistoryListBox.TabIndex = 9;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Font = new Font("Segoe UI", 12F);
            label5.Location = new Point(244, 157);
            label5.Name = "label5";
            label5.Size = new Size(97, 21);
            label5.TabIndex = 10;
            label5.Text = "Chat history:";
            // 
            // ServerPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(613, 452);
            Controls.Add(label5);
            Controls.Add(ChatHistoryListBox);
            Controls.Add(label4);
            Controls.Add(BtnConnectNewUser);
            Controls.Add(label3);
            Controls.Add(CmbChannels);
            Controls.Add(label2);
            Controls.Add(BtnChangeChannel);
            Controls.Add(BtnKickUser);
            Controls.Add(UsersListBox);
            Controls.Add(label1);
            Name = "ServerPanel";
            Text = "Chatbox Server Panel [Demo]";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private ListBox UsersListBox;
        private Button BtnKickUser;
        private Button BtnChangeChannel;
        private Label label2;
        private ComboBox CmbChannels;
        private Label label3;
        private Button BtnConnectNewUser;
        private Label label4;
        private ListBox ChatHistoryListBox;
        private Label label5;
    }
}
