using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BulkNotes
{
    /// <summary>
    /// Chooses the projects whose notes are listed together (the user's request, 1.4): a list of
    /// every project with tick boxes, All, None, OK and Cancel. At least one must stay ticked.
    /// </summary>
    public sealed class ProjectChooserForm : Form
    {
        private readonly IList<INoteProject> all;
        private readonly Strings s;
        private readonly CheckedListBox list = new CheckedListBox();

        public ProjectChooserForm(IList<INoteProject> all, IList<INoteProject> chosen, Strings strings, Font font)
        {
            this.all = all;
            s = strings;
            Text = s.ProjectsTitle;
            Name = "ProjectChooserForm";
            Font = font;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 420);
            MinimumSize = new Size(360, 300);

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(10);
            table.ColumnCount = 1;
            table.RowCount = 3;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label();
            intro.AutoSize = true;
            intro.Text = s.ProjectsIntro;
            intro.Margin = new Padding(3, 0, 3, 6);

            list.Name = "ProjectList";
            list.AccessibleName = "ProjectList";
            list.Dock = DockStyle.Fill;
            list.CheckOnClick = true;
            list.IntegralHeight = false;
            foreach (INoteProject p in all)
            {
                string name = p.ShortName;
                if (!string.IsNullOrEmpty(p.LongName) && p.LongName != name) name += " - " + p.LongName;
                list.Items.Add(name, chosen.Contains(p));
            }

            var buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.Margin = new Padding(0, 8, 0, 0);
            Button btnAll = NewButton(s.ProjectsAll, "All");
            Button btnNone = NewButton(s.ProjectsNone, "None");
            Button btnOk = NewButton(s.Ok, "OK");
            Button btnCancel = NewButton(s.Cancel, "Cancel");
            btnOk.Margin = new Padding(30, 0, 8, 0);
            buttons.Controls.Add(btnAll);
            buttons.Controls.Add(btnNone);
            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnCancel);
            btnAll.Click += delegate { TickAll(true); };
            btnNone.Click += delegate { TickAll(false); };
            btnOk.Click += delegate
            {
                if (list.CheckedIndices.Count == 0)
                {
                    MessageBox.Show(this, s.NeedOneProject, s.ProjectsTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
            };
            btnCancel.DialogResult = DialogResult.Cancel;
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            table.Controls.Add(intro, 0, 0);
            table.Controls.Add(list, 0, 1);
            table.Controls.Add(buttons, 0, 2);
            Controls.Add(table);
        }

        private static Button NewButton(string text, string name)
        {
            var b = new Button();
            b.Text = text;
            b.Name = name;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(8, 2, 8, 2);
            b.Margin = new Padding(0, 0, 8, 0);
            return b;
        }

        private void TickAll(bool on)
        {
            for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, on);
        }

        /// <summary>The projects ticked, in the list's order.</summary>
        public List<INoteProject> Chosen
        {
            get { return list.CheckedIndices.Cast<int>().Select(i => all[i]).ToList(); }
        }
    }
}
