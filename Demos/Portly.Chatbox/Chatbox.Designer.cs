namespace Portly.Chatbox
{
    partial class Chatbox
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            ChatListBox = new ListBox();
            comboBox1 = new ComboBox();
            label2 = new Label();
            TextInputBox = new TextBox();
            UsersListBox = new ListBox();
            label3 = new Label();
            BtnSend = new Button();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 12F);
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(67, 21);
            label1.TabIndex = 0;
            label1.Text = "Chatbox";
            // 
            // ChatListBox
            // 
            ChatListBox.FormattingEnabled = true;
            ChatListBox.Location = new Point(12, 33);
            ChatListBox.Name = "ChatListBox";
            ChatListBox.Size = new Size(481, 439);
            ChatListBox.TabIndex = 1;
            // 
            // comboBox1
            // 
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(334, 7);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(159, 23);
            comboBox1.TabIndex = 2;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Segoe UI", 12F);
            label2.Location = new Point(262, 7);
            label2.Name = "label2";
            label2.Size = new Size(70, 21);
            label2.TabIndex = 3;
            label2.Text = "Channel:";
            // 
            // TextInputBox
            // 
            TextInputBox.Location = new Point(12, 478);
            TextInputBox.Multiline = true;
            TextInputBox.Name = "TextInputBox";
            TextInputBox.Size = new Size(481, 47);
            TextInputBox.TabIndex = 4;
            // 
            // UsersListBox
            // 
            UsersListBox.FormattingEnabled = true;
            UsersListBox.Location = new Point(499, 33);
            UsersListBox.Name = "UsersListBox";
            UsersListBox.Size = new Size(216, 439);
            UsersListBox.TabIndex = 5;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 12F);
            label3.Location = new Point(499, 11);
            label3.Name = "label3";
            label3.Size = new Size(52, 21);
            label3.TabIndex = 6;
            label3.Text = "Users:";
            // 
            // BtnSend
            // 
            BtnSend.Location = new Point(499, 477);
            BtnSend.Name = "BtnSend";
            BtnSend.Size = new Size(216, 48);
            BtnSend.TabIndex = 7;
            BtnSend.Text = "Send";
            BtnSend.UseVisualStyleBackColor = true;
            // 
            // Chatbox
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(727, 532);
            Controls.Add(BtnSend);
            Controls.Add(label3);
            Controls.Add(UsersListBox);
            Controls.Add(TextInputBox);
            Controls.Add(label2);
            Controls.Add(comboBox1);
            Controls.Add(ChatListBox);
            Controls.Add(label1);
            Name = "Chatbox";
            Text = "Chatbox [Demo]";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private ListBox ChatListBox;
        private ComboBox comboBox1;
        private Label label2;
        private TextBox TextInputBox;
        private ListBox UsersListBox;
        private Label label3;
        private Button BtnSend;
    }
}