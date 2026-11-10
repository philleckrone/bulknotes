using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace BulkNotes
{
    /// <summary>
    /// Lists the notes of a verse, a chapter, a book or the whole project, of one or several
    /// projects, filtered by status, read, author, assignment, date, words and tag, and resolves or
    /// assigns the notes the user ticks.
    /// </summary>
    public sealed class BulkNotesForm : Form
    {
        private static readonly Color Red = Color.FromArgb(0x8A, 0x1C, 0x00);
        /// <summary>Light green for resolved notes (the user's request).</summary>
        private static readonly Color ResolvedBack = Color.FromArgb(0xCC, 0xEE, 0xCC);
        /// <summary>Yellow for Wordlist and Biblical Terms notes (the user's request, 1.3).</summary>
        private static readonly Color WordlistBack = Color.FromArgb(0xFF, 0xF2, 0xA8);
        /// <summary>Light red for unresolved conflict notes (the user's request, 1.3).</summary>
        private static readonly Color ConflictBack = Color.FromArgb(0xF8, 0xCC, 0xCC);
        private static readonly Color GreyText = Color.FromArgb(0x80, 0x80, 0x80);

        private readonly INoteHost host;
        private readonly Strings s;
        private readonly Settings settings;
        /// <summary>The projects whose notes are listed together (the user's request, 1.4), in the host's order.</summary>
        private List<INoteProject> projects = new List<INoteProject>();
        /// <summary>Each project's tags (commentTags.xml plus the built-in ones); null for a project whose tags are unknown.</summary>
        private Dictionary<INoteProject, IList<TagInfo>> tagLists = new Dictionary<INoteProject, IList<TagInfo>>();
        /// <summary>Unread notes are bold, as in Paratext's Notes list (the user's choice, 1.4).</summary>
        private Font boldFont;
        /// <summary>The notes read for the place shown, in reference order.</summary>
        private List<NoteThread> read = new List<NoteThread>();
        /// <summary>The notes listed: read, filtered and sorted.</summary>
        private List<NoteThread> shown = new List<NoteThread>();
        /// <summary>Note key -> position in reference order (for sorting).</summary>
        private Dictionary<string, int> order = new Dictionary<string, int>();
        /// <summary>Keys of the ticked notes (only notes listed stay ticked).</summary>
        private readonly HashSet<string> ticked = new HashSet<string>();
        /// <summary>True while controls are being filled (their events do nothing then).</summary>
        private bool filling;
        /// <summary>The note whose comments the list below the grid shows.</summary>
        private NoteThread listThread;
        private Action<int, int, int> referenceHandler;
        private int startBook;
        private int startChapter;
        private int startVerse;
        /// <summary>Project, scope, book and chapter of the notes read (ticks are kept while it stays the same).</summary>
        private string loadedPlace;
        private string sortColumn;
        private bool sortDescending;
        private readonly Timer textTimer = new Timer();

        private readonly Button btnProjects = new Button();
        private readonly Label lblWarning = new Label();
        private readonly Button btnFilters = new Button();
        private readonly Label lblFilters = new Label();
        /// <summary>The two filter rows, hidden by "Hide filters".</summary>
        private FlowLayoutPanel filters1;
        private FlowLayoutPanel filters2;
        private readonly Label lblIntro = new Label();
        private readonly ComboBox cboScope = new ComboBox();
        private readonly ComboBox cboBook = new ComboBox();
        private readonly ComboBox cboChapter = new ComboBox();
        private readonly ComboBox cboVerse = new ComboBox();
        private readonly Button btnPrev = new Button();
        private readonly Button btnNext = new Button();
        private readonly CheckBox chkFollow = new CheckBox();
        private readonly ComboBox cboStatus = new ComboBox();
        private readonly ComboBox cboRead = new ComboBox();
        private readonly ComboBox cboCreated = new ComboBox();
        private readonly ComboBox cboCommented = new ComboBox();
        private readonly ComboBox cboAssigned = new ComboBox();
        private readonly ComboBox cboDateField = new ComboBox();
        private readonly DateTimePicker dtpFrom = new DateTimePicker();
        private readonly DateTimePicker dtpTo = new DateTimePicker();
        private readonly TextBox txtText = new TextBox();
        private readonly ComboBox cboTag = new ComboBox();
        private readonly Button btnClear = new Button();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label lblCounts = new Label();
        private readonly Label lblComments = new Label();
        private readonly ListBox lstComments = new ListBox();
        private readonly Button btnGoTo = new Button();
        private readonly Button btnTick = new Button();
        private readonly Button btnUntick = new Button();
        private readonly Button btnMarkRead = new Button();
        private readonly Button btnMarkUnread = new Button();
        private readonly TextBox txtComment = new TextBox();
        private readonly Button btnResolve = new Button();
        private readonly ComboBox cboAssignTo = new ComboBox();
        private readonly Button btnAssign = new Button();
        private readonly Button btnClose = new Button();
        private readonly Label lblResults = new Label();
        private readonly TextBox txtResults = new TextBox();

        public BulkNotesForm(INoteHost host, Strings strings)
        {
            this.host = host;
            s = strings;
            settings = Settings.Load(host.SettingsFile);
            startBook = host.ActiveBook;
            startChapter = host.ActiveChapter;
            startVerse = host.ActiveVerse;
            BuildLayout();
            Load += delegate { Start(); };
            referenceHandler = OnReferenceChanged;
            host.ReferenceChanged += referenceHandler;
            FormClosed += delegate
            {
                host.ReferenceChanged -= referenceHandler;
                textTimer.Dispose();
            };
        }

        // ------------------------------------------------------------ layout

        private void BuildLayout()
        {
            Text = s.Title + " " + Strings.ToolVersion;
            Name = "BulkNotesForm";
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1180, area.Width - 40), Math.Min(800, area.Height - 60));
            MinimumSize = new Size(820, 600);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(10);
            table.ColumnCount = 1;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 15; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles[7] = new RowStyle(SizeType.Percent, 55f);
            table.RowStyles[10] = new RowStyle(SizeType.Percent, 25f);
            table.RowStyles[14] = new RowStyle(SizeType.Percent, 20f);

            boldFont = new Font(Font, FontStyle.Bold);

            // Projects (one or several, the user's request, 1.4)
            var top = NewFlow();
            SetupButton(btnProjects, "", "Projects");
            btnProjects.Font = boldFont;
            btnProjects.AutoSize = false;
            btnProjects.Size = new Size(420, 27);
            btnProjects.TextAlign = ContentAlignment.MiddleLeft;
            btnProjects.AutoEllipsis = true;
            top.Controls.Add(NewLabel(s.ProjectLabel));
            top.Controls.Add(btnProjects);
            btnProjects.Click += delegate { ChooseProjects(); };

            ConfigureWrappingLabel(lblWarning, table);
            lblWarning.ForeColor = Red;
            lblWarning.Name = "Warning";
            lblWarning.Visible = false;

            ConfigureWrappingLabel(lblIntro, table);
            lblIntro.Text = s.Intro;

            // Scope, book, chapter, verse
            var nav = NewFlow();
            SetupCombo(cboScope, "Scope", 120);
            cboScope.Items.AddRange(new object[] { s.ScopeVerse, s.ScopeChapter, s.ScopeBook, s.ScopeProject });
            cboScope.SelectedIndex = (int)settings.Scope;
            SetupCombo(cboBook, "Book", 80);
            SetupCombo(cboChapter, "Chapter", 60);
            SetupCombo(cboVerse, "Verse", 60);
            SetupButton(btnPrev, s.Previous, "Previous");
            SetupButton(btnNext, s.Next, "Next");
            SetupCheck(chkFollow, s.Follow, "Follow", settings.Follow);
            nav.Controls.Add(NewLabel(s.ShowLabel));
            nav.Controls.Add(cboScope);
            nav.Controls.Add(NewLabel(s.BookLabel));
            nav.Controls.Add(cboBook);
            nav.Controls.Add(NewLabel(s.ChapterLabel));
            nav.Controls.Add(cboChapter);
            nav.Controls.Add(NewLabel(s.VerseLabel));
            nav.Controls.Add(cboVerse);
            nav.Controls.Add(btnPrev);
            nav.Controls.Add(btnNext);
            nav.Controls.Add(chkFollow);
            cboScope.SelectedIndexChanged += delegate
            {
                if (filling) return;
                settings.Scope = CurrentScope;
                settings.Save();
                EnableNav();
                Reload();
            };
            cboBook.SelectedIndexChanged += delegate { if (!filling) BookChosen(1, 1); };
            cboChapter.SelectedIndexChanged += delegate { if (!filling) ChapterChosen(1); };
            cboVerse.SelectedIndexChanged += delegate { if (!filling && CurrentScope == Scope.Verse) Reload(); };
            btnPrev.Click += delegate { MoveBy(-1); };
            btnNext.Click += delegate { MoveBy(1); };
            chkFollow.CheckedChanged += delegate { settings.Follow = chkFollow.Checked; settings.Save(); };

            // Filters: status and people
            filters1 = NewFlow();
            SetupCombo(cboStatus, "Status", 120);
            cboStatus.Items.AddRange(new object[] { s.StatusUnresolved, s.StatusResolved, s.StatusAll });
            cboStatus.SelectedIndex = (int)settings.Status;
            SetupCombo(cboRead, "Read", 110);
            cboRead.Items.AddRange(new object[] { s.ReadAny, s.ReadUnread, s.ReadRead });
            cboRead.SelectedIndex = (int)settings.Read;
            SetupCombo(cboCreated, "CreatedBy", 170);
            SetupCombo(cboCommented, "CommentedBy", 170);
            SetupCombo(cboAssigned, "AssignedTo", 170);
            filters1.Controls.Add(NewLabel(s.StatusLabel));
            filters1.Controls.Add(cboStatus);
            filters1.Controls.Add(NewLabel(s.ReadLabel));
            filters1.Controls.Add(cboRead);
            filters1.Controls.Add(NewLabel(s.CreatedByLabel));
            filters1.Controls.Add(cboCreated);
            filters1.Controls.Add(NewLabel(s.CommentedByLabel));
            filters1.Controls.Add(cboCommented);
            filters1.Controls.Add(NewLabel(s.AssignedToLabel));
            filters1.Controls.Add(cboAssigned);
            cboStatus.SelectedIndexChanged += delegate
            {
                if (filling) return;
                StatusFilter before = settings.Status;
                settings.Status = (StatusFilter)cboStatus.SelectedIndex;
                settings.Save();
                // Resolved notes are read only when they are wanted.
                if (before == StatusFilter.Unresolved || settings.Status == StatusFilter.Unresolved) Reload();
                else FillGrid();
            };
            cboRead.SelectedIndexChanged += delegate
            {
                if (filling) return;
                settings.Read = (ReadFilter)cboRead.SelectedIndex;
                settings.Save();
                FillGrid();
            };
            cboCreated.SelectedIndexChanged += delegate { if (!filling) FillGrid(); };
            cboCommented.SelectedIndexChanged += delegate { if (!filling) FillGrid(); };
            cboAssigned.SelectedIndexChanged += delegate { if (!filling) FillGrid(); };

            // Filters: dates and words
            filters2 = NewFlow();
            SetupCombo(cboDateField, "DateField", 150);
            cboDateField.Items.AddRange(new object[] { s.DateCreated, s.DateLast });
            cboDateField.SelectedIndex = (int)settings.DateField;
            SetupDate(dtpFrom, "From");
            SetupDate(dtpTo, "To");
            txtText.Name = "Words";
            txtText.AccessibleName = "Words";
            txtText.Width = 200;
            txtText.Margin = new Padding(0, 3, 14, 3);
            SetupButton(btnClear, s.ClearFilters, "ClearFilters");
            filters2.Controls.Add(cboDateField);
            filters2.Controls.Add(NewLabel(s.FromLabel));
            filters2.Controls.Add(dtpFrom);
            filters2.Controls.Add(NewLabel(s.ToLabel));
            filters2.Controls.Add(dtpTo);
            filters2.Controls.Add(NewLabel(s.WordsLabel));
            filters2.Controls.Add(txtText);
            SetupCombo(cboTag, "Tag", 170);
            filters2.Controls.Add(NewLabel(s.TagLabel));
            filters2.Controls.Add(cboTag);
            cboTag.SelectedIndexChanged += delegate { if (!filling) FillGrid(); };
            filters2.Controls.Add(btnClear);
            cboDateField.SelectedIndexChanged += delegate
            {
                if (filling) return;
                settings.DateField = (DateField)cboDateField.SelectedIndex;
                settings.Save();
                FillGrid();
            };
            dtpFrom.ValueChanged += delegate { if (!filling) FillGrid(); };
            dtpTo.ValueChanged += delegate { if (!filling) FillGrid(); };
            // Typing filters after a short pause, not at every letter.
            textTimer.Interval = 350;
            textTimer.Tick += delegate { textTimer.Stop(); FillGrid(); };
            txtText.TextChanged += delegate { if (!filling) { textTimer.Stop(); textTimer.Start(); } };
            btnClear.Click += delegate { ClearFilters(); };

            // Each filter row is its own row of the window's table: a nested auto-sized table grew
            // tall and squashed the list.
            filters1.Visible = !settings.HideFilters;
            filters2.Visible = !settings.HideFilters;

            // "Hide filters" and, always shown, the filters in effect (the user's request, 1.3)
            var summary = new TableLayoutPanel();
            summary.AutoSize = true;
            summary.Dock = DockStyle.Fill;
            summary.Margin = new Padding(0, 4, 0, 0);
            summary.ColumnCount = 2;
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            SetupButton(btnFilters, settings.HideFilters ? s.ShowFilters : s.HideFilters, "ToggleFilters");
            lblFilters.Name = "FilterSummary";
            lblFilters.AutoSize = true;
            lblFilters.Font = new Font(Font, FontStyle.Bold);
            lblFilters.Margin = new Padding(3, 6, 3, 2);
            summary.Controls.Add(btnFilters, 0, 0);
            summary.Controls.Add(lblFilters, 1, 0);
            summary.SizeChanged += delegate
            {
                // Wraps beside the button, so a long list never widens the window.
                lblFilters.MaximumSize = new Size(Math.Max(200, summary.ClientSize.Width - btnFilters.Width - 30), 0);
            };
            btnFilters.Click += delegate { ToggleFilters(); };

            BuildGrid();

            lblCounts.AutoSize = true;
            lblCounts.Name = "Counts";
            lblCounts.Margin = new Padding(3, 4, 3, 2);
            var countsRow = NewFlow();
            countsRow.Margin = new Padding(0);
            countsRow.Controls.Add(lblCounts);
            countsRow.Controls.Add(LegendChip(s.LegendResolved, ResolvedBack));
            countsRow.Controls.Add(LegendChip(s.LegendWordlist, WordlistBack));
            countsRow.Controls.Add(LegendChip(s.LegendConflict, ConflictBack));
            Label unread = LegendChip(s.LegendUnread, SystemColors.Window);
            unread.Font = boldFont;
            countsRow.Controls.Add(unread);

            lblComments.AutoSize = true;
            lblComments.Font = new Font(Font, FontStyle.Bold);
            lblComments.Text = s.CommentsLabel;
            lblComments.Margin = new Padding(3, 6, 3, 2);

            lstComments.Dock = DockStyle.Fill;
            lstComments.Name = "Comments";
            lstComments.AccessibleName = "Comments";
            lstComments.IntegralHeight = false;
            lstComments.HorizontalScrollbar = true;

            var tickRow = NewFlow();
            tickRow.Margin = new Padding(0, 6, 0, 0);
            SetupButton(btnGoTo, s.GoToButton, "GoTo");
            SetupButton(btnTick, s.TickAll, "TickAll");
            SetupButton(btnUntick, s.UntickAll, "UntickAll");
            tickRow.Controls.Add(btnGoTo);
            tickRow.Controls.Add(btnTick);
            tickRow.Controls.Add(btnUntick);
            // Marking read (1.5) or unread (1.6) adds no comment and changes only the user's own read
            // status, so these sit with the tick buttons, away from the comment box, and work for
            // observers too. Bold like Resolve and Assign (the user's request, 1.6).
            SetupButton(btnMarkRead, string.Format(s.MarkReadButton, 0), "MarkRead");
            btnMarkRead.Font = boldFont;
            SetupButton(btnMarkUnread, string.Format(s.MarkUnreadButton, 0), "MarkUnread");
            btnMarkUnread.Font = boldFont;
            tickRow.Controls.Add(btnMarkRead);
            tickRow.Controls.Add(btnMarkUnread);
            tickRow.Controls.Add(btnClose);
            btnGoTo.Click += delegate { GoToSelected(); };
            btnTick.Click += delegate { TickAll(true); };
            btnUntick.Click += delegate { TickAll(false); };
            btnMarkRead.Click += delegate { RunOnTicked(BulkAction.MarkRead); };
            btnMarkUnread.Click += delegate { RunOnTicked(BulkAction.MarkUnread); };

            // What to do with the ticked notes
            var actions = NewFlow();
            actions.Margin = new Padding(0, 2, 0, 6);
            txtComment.Name = "Comment";
            txtComment.AccessibleName = "Comment";
            txtComment.Width = 260;
            txtComment.Margin = new Padding(0, 3, 14, 3);
            SetupButton(btnResolve, string.Format(s.ResolveButton, 0), "Resolve");
            btnResolve.Font = new Font(Font, FontStyle.Bold);
            SetupCombo(cboAssignTo, "AssignTo", 190);
            SetupButton(btnAssign, string.Format(s.AssignButton, 0), "Assign");
            btnAssign.Font = new Font(Font, FontStyle.Bold);
            SetupButton(btnClose, s.Close, "Close");
            actions.Controls.Add(NewLabel(s.CommentLabel));
            actions.Controls.Add(txtComment);
            actions.Controls.Add(btnResolve);
            actions.Controls.Add(NewLabel(s.AssignToLabel));
            actions.Controls.Add(cboAssignTo);
            actions.Controls.Add(btnAssign);
            btnResolve.Click += delegate { RunOnTicked(BulkAction.Resolve); };
            btnAssign.Click += delegate { RunOnTicked(BulkAction.Assign); };
            btnClose.Click += delegate { Close(); };

            lblResults.AutoSize = true;
            lblResults.Font = new Font(Font, FontStyle.Bold);
            lblResults.Text = s.ResultsLabel;
            lblResults.Margin = new Padding(3, 4, 3, 2);
            txtResults.Multiline = true;
            txtResults.ReadOnly = true;
            txtResults.ScrollBars = ScrollBars.Both;
            txtResults.WordWrap = false;
            txtResults.Dock = DockStyle.Fill;
            txtResults.Name = "Results";
            txtResults.AccessibleName = "Results";
            txtResults.BackColor = SystemColors.Window;

            table.Controls.Add(top, 0, 0);
            table.Controls.Add(lblWarning, 0, 1);
            table.Controls.Add(lblIntro, 0, 2);
            table.Controls.Add(nav, 0, 3);
            table.Controls.Add(summary, 0, 4);
            table.Controls.Add(filters1, 0, 5);
            table.Controls.Add(filters2, 0, 6);
            table.Controls.Add(grid, 0, 7);
            table.Controls.Add(countsRow, 0, 8);
            table.Controls.Add(lblComments, 0, 9);
            table.Controls.Add(lstComments, 0, 10);
            table.Controls.Add(tickRow, 0, 11);
            table.Controls.Add(actions, 0, 12);
            table.Controls.Add(lblResults, 0, 13);
            table.Controls.Add(txtResults, 0, 14);
            Controls.Add(table);
            EnableNav();
        }

        /// <summary>A small coloured label for the colour key.</summary>
        private static Label LegendChip(string text, Color back)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.BackColor = back;
            l.Padding = new Padding(4, 1, 4, 1);
            l.Margin = new Padding(14, 4, 0, 2);
            return l;
        }

        private void ToggleFilters()
        {
            settings.HideFilters = !settings.HideFilters;
            settings.Save();
            filters1.Visible = !settings.HideFilters;
            filters2.Visible = !settings.HideFilters;
            btnFilters.Text = settings.HideFilters ? s.ShowFilters : s.HideFilters;
        }

        /// <summary>The filters in effect, in words, for the line beside "Hide filters".</summary>
        private void UpdateFilterSummary()
        {
            var parts = new List<string>();
            parts.Add(s.StatusLabel + " " + cboStatus.Text);
            if (cboRead.SelectedIndex > 0) parts.Add(s.ReadLabel + " " + cboRead.Text);
            AddPerson(parts, s.CreatedByLabel, cboCreated);
            AddPerson(parts, s.CommentedByLabel, cboCommented);
            AddPerson(parts, s.AssignedToLabel, cboAssigned);
            if (dtpFrom.Checked || dtpTo.Checked)
            {
                string d = cboDateField.Text;
                if (dtpFrom.Checked) d += " " + string.Format(s.SumFrom, dtpFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                if (dtpTo.Checked) d += " " + string.Format(s.SumTo, dtpTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                parts.Add(d);
            }
            if (txtText.Text.Trim().Length > 0) parts.Add(s.WordsLabel + " \"" + txtText.Text.Trim() + "\"");
            var tag = cboTag.SelectedItem as TagInfo;
            if (tag != null) parts.Add(s.TagLabel + " " + tag.Name);
            lblFilters.Text = string.Format(s.FiltersSummary, string.Join(s.SumSeparator, parts.ToArray()));
        }

        private static void AddPerson(List<string> parts, string label, ComboBox c)
        {
            var item = c.SelectedItem as PersonItem;
            if (item != null && item.Value != null) parts.Add(label + " " + item.Text);
        }

        private static FlowLayoutPanel NewFlow()
        {
            var f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = true;
            f.Dock = DockStyle.Fill;
            f.Margin = new Padding(0, 2, 0, 2);
            return f;
        }

        private static Label NewLabel(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(3, 7, 3, 0);
            return l;
        }

        private static void SetupCombo(ComboBox c, string name, int width)
        {
            c.Name = name;
            c.AccessibleName = name;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = width;
            c.Margin = new Padding(0, 3, 14, 3);
            c.MaxDropDownItems = 25;
        }

        private static void SetupDate(DateTimePicker d, string name)
        {
            d.Name = name;
            d.AccessibleName = name;
            d.Format = DateTimePickerFormat.Custom;
            d.CustomFormat = "yyyy-MM-dd";
            d.ShowCheckBox = true;   // unticked = no limit
            d.Checked = false;
            d.Width = 125;
            d.Margin = new Padding(0, 3, 14, 3);
        }

        private static void SetupCheck(CheckBox c, string text, string name, bool value)
        {
            c.Text = text;
            c.Name = name;
            c.AutoSize = true;
            c.Checked = value;
            c.Margin = new Padding(10, 6, 3, 0);
        }

        private static void SetupButton(Button b, string text, string name)
        {
            b.Text = text;
            b.Name = name;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(8, 2, 8, 2);
            b.Margin = new Padding(0, 2, 8, 2);
        }

        private void ConfigureWrappingLabel(Label label, TableLayoutPanel table)
        {
            label.AutoSize = true;
            label.Margin = new Padding(3, 2, 3, 2);
            // Wraps at the window's width, so a long text never widens the layout past the window.
            label.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 50), 0);
            EventHandler fit = delegate
            {
                label.MaximumSize = new Size(Math.Max(200, table.ClientSize.Width - table.Padding.Horizontal - 10), 0);
            };
            table.SizeChanged += fit;
            Load += fit;
        }

        private void BuildGrid()
        {
            grid.Name = "Notes";
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.BackgroundColor = SystemColors.Window;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.ShowCellToolTips = true;
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            var check = new DataGridViewCheckBoxColumn();
            check.Name = "Tick";
            check.HeaderText = s.ColTick;
            check.Width = 62;
            check.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(check);
            AddTextColumn("Ref", s.ColRef, 95);
            AddTextColumn("Project", s.ColProject, 80);
            grid.Columns["Project"].Visible = false;   // shown when several projects are listed
            AddTextColumn("Status", s.ColStatus, 80);
            AddTextColumn("Read", s.ColRead, 50);
            AddTextColumn("Assigned", s.ColAssigned, 120);
            AddTextColumn("CreatedBy", s.ColCreatedBy, 120);
            AddTextColumn("Created", s.ColCreated, 85);
            AddTextColumn("LastBy", s.ColLastBy, 120);
            AddTextColumn("LastDate", s.ColLastDate, 85);
            AddTextColumn("Count", s.ColCount, 95);
            AddTextColumn("Tags", s.ColTags, 120);
            AddTextColumn("Text", s.ColText, 300);
            grid.Columns["Text"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (filling || e.RowIndex < 0 || e.RowIndex >= shown.Count) return;
                if (grid.Columns[e.ColumnIndex].Name != "Tick") return;
                bool on = Equals(grid.Rows[e.RowIndex].Cells["Tick"].Value, true);
                if (on) ticked.Add(shown[e.RowIndex].Key);
                else ticked.Remove(shown[e.RowIndex].Key);
                UpdateCounts();
            };
            // Not SelectionChanged: it can fire before CurrentCell moves to the new row.
            grid.CurrentCellChanged += delegate
            {
                if (!filling && SelectedThread != listThread) ShowComments();
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name != "Tick") GoToSelected();
            };
            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                string name = grid.Columns[e.ColumnIndex].Name;
                if (name == "Tick") return;
                if (sortColumn == name) sortDescending = !sortDescending;
                else
                {
                    sortColumn = name;
                    sortDescending = false;
                }
                FillGrid();
            };
        }

        private void AddTextColumn(string name, string header, int width)
        {
            var c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.Width = width;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.Programmatic;
            grid.Columns.Add(c);
        }

        private Scope CurrentScope
        {
            get { return cboScope.SelectedIndex < 0 ? Scope.Chapter : (Scope)cboScope.SelectedIndex; }
        }

        private void EnableNav()
        {
            cboBook.Enabled = CurrentScope != Scope.Project;
            cboChapter.Enabled = CurrentScope == Scope.Verse || CurrentScope == Scope.Chapter;
            cboVerse.Enabled = CurrentScope == Scope.Verse;
            btnPrev.Enabled = CurrentScope != Scope.Project;
            btnNext.Enabled = CurrentScope != Scope.Project;
        }

        // ------------------------------------------------------------ projects

        private void Start()
        {
            IList<INoteProject> all = host.Projects;
            // The projects ticked last time, plus the one the tool was started from.
            var chosen = all.Where(p => settings.Projects.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (host.Active != null && !chosen.Contains(host.Active))
            {
                INoteProject active = all.FirstOrDefault(p => SameId(p.Id, host.Active.Id));
                if (active != null) chosen.Add(active);
            }
            if (chosen.Count == 0 && all.Count > 0) chosen.Add(all[0]);
            SetProjects(chosen);
        }

        private static bool SameId(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The Projects button: tick the projects whose notes are listed together.</summary>
        private void ChooseProjects()
        {
            using (var dlg = new ProjectChooserForm(host.Projects, projects, s, Font))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var current = cboBook.SelectedItem as BookInfo;
                startBook = current != null ? current.Book : startBook;
                startChapter = CurrentChapter;
                startVerse = CurrentVerse;
                SetProjects(dlg.Chosen);
            }
        }

        /// <summary>Lists the notes of these projects (in the host's order) and remembers the choice.</summary>
        private void SetProjects(IList<INoteProject> chosen)
        {
            IList<INoteProject> all = host.Projects;
            projects = all.Where(p => chosen.Contains(p)).ToList();
            settings.Projects.Clear();
            settings.Projects.AddRange(projects.Select(p => p.Id));
            settings.Save();
            btnProjects.Text = string.Join(", ", projects.Select(p => p.ShortName).ToArray()) + "...";
            grid.Columns["Project"].Visible = projects.Count > 1;

            tagLists = NoteReader.TagLists(projects, s);
            var warnings = new List<string>();
            if (projects.Count == 0) warnings.Add(s.OpenProjectFirst);
            string readOnly = Names(projects.Where(p => !p.CanEditNotes));
            if (readOnly.Length > 0) warnings.Add(string.Format(s.CannotEdit, readOnly));
            string noTags = Names(projects.Where(p => tagLists[p] == null));
            if (noTags.Length > 0) warnings.Add(string.Format(s.NoTagsInfo, noTags));
            lblWarning.Text = string.Join("  ", warnings.ToArray());
            lblWarning.Visible = lblWarning.Text.Length > 0;
            FillTags();
            FillAssignTo();
            FillBooks();
        }

        private static string Names(IEnumerable<INoteProject> list)
        {
            return string.Join(", ", list.Select(p => p.ShortName).ToArray());
        }

        /// <summary>Fills the Tag filter with the tag names of all the projects (ids differ between
        /// projects, names are what the user sees), keeping the choice when the name still exists.</summary>
        private void FillTags()
        {
            var item = cboTag.SelectedItem as TagInfo;
            string before = item != null ? item.Name : null;
            filling = true;
            cboTag.Items.Clear();
            cboTag.Items.Add(s.AnyTag);
            var names = new List<string>();
            foreach (INoteProject p in projects)
            {
                IList<TagInfo> tags = tagLists[p];
                if (tags == null) continue;
                foreach (TagInfo t in tags)
                    if (!names.Contains(t.Name, StringComparer.OrdinalIgnoreCase)) names.Add(t.Name);
            }
            foreach (string n in names) cboTag.Items.Add(new TagInfo { Name = n });
            int pick = 0;
            for (int i = 1; i < cboTag.Items.Count; i++)
                if (before != null && string.Equals(((TagInfo)cboTag.Items[i]).Name, before, StringComparison.OrdinalIgnoreCase)) pick = i;
            cboTag.SelectedIndex = pick;
            cboTag.Enabled = names.Count > 0;
            filling = false;
        }

        /// <summary>Team, anyone active in at least one of the projects, and nobody (the user's
        /// rule, 1.4: a note in a project where the person is not active is skipped).</summary>
        private void FillAssignTo()
        {
            string before = cboAssignTo.SelectedItem is PersonItem ? ((PersonItem)cboAssignTo.SelectedItem).Value : null;
            cboAssignTo.Items.Clear();
            var names = new List<string> { Strings.Team };
            foreach (INoteProject p in projects)
                foreach (string u in p.Users)
                    if (!string.IsNullOrEmpty(u) && !names.Contains(u, StringComparer.OrdinalIgnoreCase)) names.Add(u);
            foreach (string n in names) cboAssignTo.Items.Add(new PersonItem(n, n == Strings.Team ? s.TeamText : n));
            cboAssignTo.Items.Add(new PersonItem(NoteFilter.Nobody, s.Nobody));
            int pick = 0;
            for (int i = 0; i < cboAssignTo.Items.Count; i++)
                if (before != null && ((PersonItem)cboAssignTo.Items[i]).Value == before) pick = i;
            cboAssignTo.SelectedIndex = pick;
        }

        /// <summary>
        /// The menu command was chosen again while the window is open: read the notes again. The
        /// project of the Paratext window the command came from is added to the projects listed,
        /// and the window moves to its place when "Follow Paratext" is ticked.
        /// </summary>
        public void RefreshFromParatext(string projectId, int book, int chapter, int verse)
        {
            INoteProject from = host.Projects.FirstOrDefault(p => SameId(p.Id, projectId));
            bool move = chkFollow.Checked && book > 0 && chapter > 0;
            if (from != null && !projects.Contains(from))
            {
                var current = cboBook.SelectedItem as BookInfo;
                startBook = move ? book : current != null ? current.Book : 0;
                startChapter = move ? chapter : CurrentChapter;
                startVerse = move ? verse : CurrentVerse;
                var chosen = new List<INoteProject>(projects);
                chosen.Add(from);
                SetProjects(chosen);
                return;
            }
            if (move && !IsShown(book, chapter, verse))
            {
                ShowPlace(book, chapter, verse);
                return;
            }
            Reload();
        }

        // ------------------------------------------------------------ book, chapter and verse

        private void FillBooks()
        {
            filling = true;
            cboBook.Items.Clear();
            var books = new SortedDictionary<int, BookInfo>();
            foreach (INoteProject p in projects)
                foreach (BookInfo b in p.Books)
                {
                    BookInfo have;
                    if (!books.TryGetValue(b.Book, out have))
                        books[b.Book] = new BookInfo { Book = b.Book, Code = b.Code, LastChapter = b.LastChapter };
                    else
                        have.LastChapter = Math.Max(have.LastChapter, b.LastChapter);
                }
            foreach (BookInfo b in books.Values) cboBook.Items.Add(b);
            int pick = -1;
            for (int i = 0; i < cboBook.Items.Count; i++)
                if (((BookInfo)cboBook.Items[i]).Book == startBook) pick = i;
            if (pick < 0 && cboBook.Items.Count > 0) pick = 0;
            cboBook.SelectedIndex = pick;
            filling = false;
            bool atStart = pick >= 0 && ((BookInfo)cboBook.Items[pick]).Book == startBook;
            BookChosen(atStart ? startChapter : 1, atStart ? startVerse : 1);
        }

        /// <summary>True when the window already shows that place.</summary>
        private bool IsShown(int book, int chapter, int verse)
        {
            if (CurrentScope == Scope.Project) return true;   // every place is shown
            var current = cboBook.SelectedItem as BookInfo;
            if (current == null || current.Book != book) return false;
            if (CurrentScope == Scope.Book) return true;
            if (CurrentChapter != chapter) return false;
            return CurrentScope != Scope.Verse || CurrentVerse == Math.Max(1, verse);
        }

        /// <summary>Moves the book, chapter and verse boxes to a place and reads its notes.</summary>
        private void ShowPlace(int book, int chapter, int verse)
        {
            for (int i = 0; i < cboBook.Items.Count; i++)
            {
                if (((BookInfo)cboBook.Items[i]).Book != book) continue;
                filling = true;
                cboBook.SelectedIndex = i;
                filling = false;
                BookChosen(chapter, verse);
                return;
            }
        }

        private void BookChosen(int chapter, int verse)
        {
            filling = true;
            cboChapter.Items.Clear();
            var book = cboBook.SelectedItem as BookInfo;
            if (book != null)
            {
                int last = Math.Max(1, book.LastChapter);
                for (int c = 1; c <= last; c++) cboChapter.Items.Add(c.ToString(CultureInfo.InvariantCulture));
                cboChapter.SelectedIndex = Math.Max(0, Math.Min(last, Math.Max(1, chapter)) - 1);
            }
            filling = false;
            ChapterChosen(verse);
        }

        /// <summary>Fills the verse box for the chapter shown, then reads the notes.</summary>
        private void ChapterChosen(int verse)
        {
            filling = true;
            cboVerse.Items.Clear();
            var book = cboBook.SelectedItem as BookInfo;
            if (book != null && projects.Count > 0)
            {
                int last = Math.Max(1, projects.Max(p => p.LastVerse(book.Book, CurrentChapter)));
                for (int v = 1; v <= last; v++) cboVerse.Items.Add(v.ToString(CultureInfo.InvariantCulture));
                cboVerse.SelectedIndex = Math.Max(0, Math.Min(last, Math.Max(1, verse)) - 1);
            }
            filling = false;
            Reload();
        }

        private int CurrentChapter
        {
            get { return cboChapter.SelectedIndex + 1; }
        }

        private int CurrentVerse
        {
            get { return cboVerse.SelectedIndex + 1; }
        }

        /// <summary>"PDT15", "MAT", "MAT 5" or "MAT 5:3", following the scope.</summary>
        private string PlaceText
        {
            get
            {
                if (CurrentScope == Scope.Project) return Names(projects);
                var book = cboBook.SelectedItem as BookInfo;
                if (book == null) return "";
                if (CurrentScope == Scope.Book) return book.Code;
                string c = book.Code + " " + CurrentChapter.ToString(CultureInfo.InvariantCulture);
                return CurrentScope == Scope.Verse ? c + ":" + CurrentVerse.ToString(CultureInfo.InvariantCulture) : c;
            }
        }

        /// <summary>Previous / Next: by verse, chapter or book, following the scope.</summary>
        private void MoveBy(int delta)
        {
            if (CurrentScope == Scope.Project) return;
            if (CurrentScope == Scope.Verse)
            {
                int v = cboVerse.SelectedIndex + delta;
                if (v >= 0 && v < cboVerse.Items.Count)
                {
                    cboVerse.SelectedIndex = v;   // reloads
                    return;
                }
            }
            if (CurrentScope != Scope.Book)
            {
                int next = cboChapter.SelectedIndex + delta;
                if (next >= 0 && next < cboChapter.Items.Count)
                {
                    filling = true;
                    cboChapter.SelectedIndex = next;
                    filling = false;
                    ChapterChosen(delta > 0 ? 1 : int.MaxValue);
                    return;
                }
            }
            int b = cboBook.SelectedIndex + delta;
            if (b < 0 || b >= cboBook.Items.Count) return;
            filling = true;
            cboBook.SelectedIndex = b;
            filling = false;
            BookChosen(delta > 0 ? 1 : int.MaxValue, delta > 0 ? 1 : int.MaxValue);
        }

        private void OnReferenceChanged(int book, int chapter, int verse)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, int, int>(OnReferenceChanged), book, chapter, verse);
                return;
            }
            if (!chkFollow.Checked || chapter < 1 || IsShown(book, chapter, verse)) return;
            ShowPlace(book, chapter, verse);
        }

        // ------------------------------------------------------------ the notes

        /// <summary>The GetNotes arguments for the scope: the chapter (Verse and Chapter scopes),
        /// chapter 0 = the whole book, book 0 = the whole project. False when nothing is chosen.</summary>
        private bool ReadArgs(out int bookNum, out int chapter)
        {
            bookNum = 0;
            chapter = 0;
            if (projects.Count == 0) return false;
            if (CurrentScope == Scope.Project) return true;
            var book = cboBook.SelectedItem as BookInfo;
            if (book == null) return false;
            bookNum = book.Book;
            if (CurrentScope == Scope.Book) return true;
            chapter = CurrentChapter;
            return chapter > 0;
        }

        /// <summary>Reads the notes of the place shown: the chapter (Verse and Chapter scopes), the
        /// whole book or the whole project. Reading the same place again keeps the ticks of the
        /// notes still listed.</summary>
        private void Reload()
        {
            int bookNum, chapter;
            bool ok = ReadArgs(out bookNum, out chapter);
            string place = ok ? string.Join(",", projects.Select(p => p.Id).ToArray()) + "|" + bookNum + "|" + chapter + "|" + CurrentScope : null;
            if (place != loadedPlace) ticked.Clear();
            loadedPlace = place;
            read = new List<NoteThread>();
            if (ok)
            {
                Cursor old = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                lblCounts.Text = string.Format(s.Reading, PlaceText);
                lblCounts.Refresh();
                try
                {
                    var errors = new List<string>();
                    read = NoteReader.Read(projects, bookNum, chapter, settings.Status != StatusFilter.Unresolved, tagLists, errors);
                    if (errors.Count > 0) txtResults.Text = string.Format(s.Error, string.Join(Environment.NewLine, errors.ToArray()));
                }
                catch (Exception ex)
                {
                    txtResults.Text = string.Format(s.Error, ex.Message);
                }
                finally
                {
                    Cursor.Current = old;
                }
            }
            order = new Dictionary<string, int>();
            for (int i = 0; i < read.Count; i++) order[read[i].Key] = i;
            FillPeople();
            FillGrid();
        }

        /// <summary>Fills the people filters from the notes read, keeping each choice.</summary>
        private void FillPeople()
        {
            filling = true;
            FillPersonCombo(cboCreated, read.Select(t => t.CreatedBy), false);
            FillPersonCombo(cboCommented, read.SelectMany(t => t.Authors), false);
            FillPersonCombo(cboAssigned, read.Where(t => t.IsAssigned).Select(t => t.AssignedUser), true);
            filling = false;
        }

        private void FillPersonCombo(ComboBox c, IEnumerable<string> names, bool withNobody)
        {
            var item = c.SelectedItem as PersonItem;
            string before = item != null ? item.Value : null;
            c.Items.Clear();
            c.Items.Add(new PersonItem(null, s.Anyone));
            if (withNobody) c.Items.Add(new PersonItem(NoteFilter.Nobody, s.Nobody));
            var list = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            // A choice stays even when no note read here has that person, so the filter still shows.
            if (!string.IsNullOrEmpty(before) && !list.Contains(before, StringComparer.OrdinalIgnoreCase)) list.Add(before);
            foreach (string n in list) c.Items.Add(new PersonItem(n, n == Strings.Team ? s.TeamText : n));
            int pick = 0;
            for (int i = 0; i < c.Items.Count; i++)
                if (((PersonItem)c.Items[i]).Value == before) pick = i;
            c.SelectedIndex = pick;
        }

        private NoteFilter CurrentFilter
        {
            get
            {
                var f = new NoteFilter();
                f.Status = (StatusFilter)Math.Max(0, cboStatus.SelectedIndex);
                f.Read = (ReadFilter)Math.Max(0, cboRead.SelectedIndex);
                f.CreatedBy = PersonValue(cboCreated);
                f.CommentedBy = PersonValue(cboCommented);
                f.AssignedTo = PersonValue(cboAssigned);
                f.DateField = (DateField)Math.Max(0, cboDateField.SelectedIndex);
                if (dtpFrom.Checked) f.From = dtpFrom.Value.Date;
                if (dtpTo.Checked) f.To = dtpTo.Value.Date;
                f.Text = txtText.Text;
                f.Verse = CurrentScope == Scope.Verse ? CurrentVerse : 0;
                var tag = cboTag.SelectedItem as TagInfo;
                if (tag != null) f.TagName = tag.Name;
                return f;
            }
        }

        private static string PersonValue(ComboBox c)
        {
            var item = c.SelectedItem as PersonItem;
            return item != null ? item.Value : null;
        }

        private void ClearFilters()
        {
            filling = true;
            cboCreated.SelectedIndex = 0;
            cboCommented.SelectedIndex = 0;
            cboAssigned.SelectedIndex = 0;
            dtpFrom.Checked = false;
            dtpTo.Checked = false;
            txtText.Text = "";
            if (cboTag.Items.Count > 0) cboTag.SelectedIndex = 0;
            filling = false;
            textTimer.Stop();
            FillGrid();
        }

        /// <summary>Lists the notes read that pass the filters, in the chosen order.</summary>
        private void FillGrid()
        {
            NoteThread current = SelectedThread;
            shown = CurrentFilter.Apply(read).ToList();
            shown.Sort(Compare);
            // Only notes listed stay ticked, so nothing hidden by a filter is changed.
            ticked.IntersectWith(shown.Select(t => t.Key));

            filling = true;
            grid.SuspendLayout();
            grid.Rows.Clear();
            var rows = new List<DataGridViewRow>(shown.Count);
            foreach (NoteThread t in shown)
            {
                var row = new DataGridViewRow();
                row.CreateCells(grid,
                    ticked.Contains(t.Key),
                    t.RangeReference,
                    t.Project != null ? t.Project.ShortName : "",
                    t.IsResolved ? s.StatusResolvedOne : s.StatusOpenOne,
                    t.IsRead ? s.ReadYes : s.ReadNo,
                    t.IsAssigned ? (t.AssignedUser == Strings.Team ? s.TeamText : t.AssignedUser) : "",
                    t.CreatedBy,
                    DateText(t.Created),
                    t.LastBy,
                    DateText(t.LastDate),
                    t.Comments.Count.ToString(CultureInfo.InvariantCulture),
                    t.TagText,
                    t.FirstText.Replace("\n", "  /  "));
                row.Cells[row.Cells.Count - 1].ToolTipText = t.FirstText;
                switch (t.Kind)
                {
                    case RowKind.Conflict: row.DefaultCellStyle.BackColor = ConflictBack; break;
                    case RowKind.WordlistOrTerm: row.DefaultCellStyle.BackColor = WordlistBack; break;
                    case RowKind.Resolved: row.DefaultCellStyle.BackColor = ResolvedBack; break;
                }
                if (!t.IsRead) row.DefaultCellStyle.Font = boldFont;
                rows.Add(row);
            }
            grid.Rows.AddRange(rows.ToArray());
            foreach (DataGridViewColumn c in grid.Columns)
                if (c.SortMode == DataGridViewColumnSortMode.Programmatic)
                    c.HeaderCell.SortGlyphDirection = c.Name == sortColumn
                        ? (sortDescending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            grid.ResumeLayout();
            filling = false;

            int at = current != null ? shown.IndexOf(current) : -1;
            if (at < 0 && shown.Count > 0) at = 0;
            if (at >= 0)
            {
                grid.CurrentCell = grid.Rows[at].Cells["Ref"];
                grid.Rows[at].Selected = true;
            }
            ShowComments();
            UpdateCounts();
            UpdateFilterSummary();
        }

        private int Compare(NoteThread a, NoteThread b)
        {
            int r = 0;
            switch (sortColumn)
            {
                case "Project": r = CompareText(a.Project != null ? a.Project.ShortName : "", b.Project != null ? b.Project.ShortName : ""); break;
                case "Status": r = a.IsResolved.CompareTo(b.IsResolved); break;
                case "Read": r = a.IsRead.CompareTo(b.IsRead); break;
                case "Assigned": r = CompareText(a.AssignedUser, b.AssignedUser); break;
                case "CreatedBy": r = CompareText(a.CreatedBy, b.CreatedBy); break;
                case "Created": r = a.Created.CompareTo(b.Created); break;
                case "LastBy": r = CompareText(a.LastBy, b.LastBy); break;
                case "LastDate": r = a.LastDate.CompareTo(b.LastDate); break;
                case "Count": r = a.Comments.Count.CompareTo(b.Comments.Count); break;
                case "Tags": r = CompareText(a.TagText, b.TagText); break;
                case "Text": r = CompareText(a.FirstText, b.FirstText); break;
            }
            if (sortDescending) r = -r;
            if (r != 0) return r;
            // Reference order (the order read), also for ties; Ref descending reverses it.
            int x, y;
            order.TryGetValue(a.Key, out x);
            order.TryGetValue(b.Key, out y);
            return sortColumn == "Ref" && sortDescending ? y.CompareTo(x) : x.CompareTo(y);
        }

        private static int CompareText(string a, string b)
        {
            return string.Compare(a ?? "", b ?? "", StringComparison.CurrentCultureIgnoreCase);
        }

        private static string DateText(DateTimeOffset d)
        {
            return d == DateTimeOffset.MinValue ? "" : d.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private NoteThread SelectedThread
        {
            get
            {
                int r = grid.CurrentCell != null ? grid.CurrentCell.RowIndex : -1;
                return r >= 0 && r < shown.Count ? shown[r] : null;
            }
        }

        private void ShowComments()
        {
            lstComments.BeginUpdate();
            lstComments.Items.Clear();
            NoteThread t = SelectedThread;
            listThread = t;
            if (t != null)
            {
                if (!string.IsNullOrEmpty(t.SelectedText))
                    lstComments.Items.Add(string.Format(s.AttachedTo, t.SelectedText));
                for (int i = 0; i < t.Comments.Count; i++)
                {
                    NoteComment c = t.Comments[i];
                    string date = c.Created.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    string line = string.Format(s.CommentLine, i + 1, c.Author, date, c.HasText ? c.Text.Replace("\n", "  /  ") : "-");
                    if (c.AssignedUser != null)
                        line += "  " + string.Format(s.AssignedMark, c.AssignedUser == Strings.Team ? s.TeamText : BulkRunner.UserText(s, c.AssignedUser));
                    lstComments.Items.Add(line);
                }
                if (t.IsResolved) lstComments.Items.Add(s.ResolvedLine);
            }
            lstComments.EndUpdate();
        }

        private void UpdateCounts()
        {
            int n = shown.Count(t => ticked.Contains(t.Key));
            string text = string.Format(s.Counts, PlaceText, read.Count, shown.Count, n);
            if (read.Count == 0) text += "  " + s.NoNotes;
            lblCounts.Text = text;
            btnResolve.Text = string.Format(s.ResolveButton, n);
            btnMarkRead.Text = string.Format(s.MarkReadButton, n);
            btnMarkUnread.Text = string.Format(s.MarkUnreadButton, n);
            btnAssign.Text = string.Format(s.AssignButton, n);
            bool canEdit = projects.Any(p => p.CanEditNotes);
            btnResolve.Enabled = canEdit;
            btnAssign.Enabled = canEdit;
        }

        private void TickAll(bool on)
        {
            grid.EndEdit();
            filling = true;
            for (int i = 0; i < shown.Count; i++)
            {
                if (on) ticked.Add(shown[i].Key);
                else ticked.Remove(shown[i].Key);
                grid.Rows[i].Cells["Tick"].Value = on;
            }
            filling = false;
            UpdateCounts();
        }

        private void GoToSelected()
        {
            NoteThread t = SelectedThread;
            if (t == null || t.Project == null) return;
            try
            {
                t.Project.GoTo(t.Book, t.Chapter, t.Verse);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(s.Error, ex.Message), s.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------ changing notes

        private void RunOnTicked(BulkAction action)
        {
            grid.EndEdit();
            if (projects.Count == 0) return;
            List<NoteThread> notes = shown.Where(t => ticked.Contains(t.Key)).ToList();
            if (notes.Count == 0)
            {
                MessageBox.Show(this, s.NothingTicked, s.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string user = null;
            string question;
            string where = Names(projects.Where(p => notes.Any(t => t.Project == p)));
            if (action == BulkAction.Resolve)
                question = string.Format(s.ConfirmResolve, notes.Count, where);
            else if (action == BulkAction.MarkRead)
                question = string.Format(s.ConfirmMarkRead, notes.Count, where);
            else if (action == BulkAction.MarkUnread)
                question = string.Format(s.ConfirmMarkUnread, notes.Count, where);
            else
            {
                var item = cboAssignTo.SelectedItem as PersonItem;
                if (item == null) return;
                user = item.Value;
                question = string.Format(s.ConfirmAssign, notes.Count, where, item.Text);
            }
            // Marking read or unread adds no comment, so the comment box is left for the next change.
            bool readOnly = action == BulkAction.MarkRead || action == BulkAction.MarkUnread;
            string comment = readOnly ? "" : txtComment.Text.Trim();
            if (comment.Length > 0) question += "\n\n" + string.Format(s.ConfirmComment, comment);
            else if (action == BulkAction.Assign) question += "\n\n" + string.Format(s.ConfirmComment, BulkRunner.AssignText(s, user));
            int already = notes.Count(t => BulkRunner.NeedsNoChange(t, action, user));
            if (already > 0) question += "\n\n" + string.Format(s.ConfirmSkip, already);
            if (MessageBox.Show(this, question, s.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                // Read back (resolved notes too) to see that Paratext kept each change.
                int bookNum, chapter;
                bool place = ReadArgs(out bookNum, out chapter);
                List<BulkReport> reports = BulkRunner.RunAll(action, notes, user, comment, s,
                    p => place ? NoteReader.Read(new[] { p }, bookNum, chapter, true, tagLists, null) : null);
                txtResults.Text = BulkRunner.DescribeAll(reports, s);
                if (!readOnly && reports.Any(r => r.Done.Count > 0)) txtComment.Text = "";
            }
            catch (Exception ex)
            {
                txtResults.Text = string.Format(s.Error, ex.Message);
            }
            finally
            {
                Cursor.Current = old;
            }
            // Nothing stays ticked, so a second click does not repeat the change.
            ticked.Clear();
            Reload();
        }

        /// <summary>A person in a list: Value null = anyone, "" = nobody.</summary>
        private sealed class PersonItem
        {
            public readonly string Value;
            public readonly string Text;

            public PersonItem(string value, string text)
            {
                Value = value;
                Text = text;
            }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
