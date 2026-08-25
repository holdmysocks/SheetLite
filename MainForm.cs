using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SheetLite;

internal sealed partial class MainForm : Form
{
    private const string SpreadsheetOpenFilter = "Spreadsheet files (*.csv;*.tsv;*.txt;*.xlsx)|*.csv;*.tsv;*.txt;*.xlsx|CSV and delimited files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Excel workbooks (*.xlsx)|*.xlsx|All files (*.*)|*.*";
    private static string AppVersion => typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private readonly DataGridView grid = new SmoothDataGridView();
    private readonly ToolStrip toolbar = new();
    private ToolStripButton boldButton = null!, italicButton = null!, underlineButton = null!;
    private ToolStripButton alignLeftButton = null!, alignCenterButton = null!, alignRightButton = null!;
    private ToolStripButton alignTopButton = null!, alignMiddleButton = null!, alignBottomButton = null!;
    private readonly StatusStrip status = new DividerStatusStrip();
    private readonly Panel contentHost = new();
    private readonly TableLayoutPanel shell = new();
    private readonly WindowBorderOverlay windowBorder = new();
    private readonly ResizeCursorMessageFilter resizeCursorFilter;
    private readonly Panel welcome = new();
    private readonly Panel scrollCorner = new(), secondaryScrollCorner = new();
    private readonly FlowLayoutPanel recentFiles = new();
    private readonly List<string> sessionRecent = [];
    private readonly ToolTip recentToolTip = new();
    private readonly List<Image> chromeGlyphs = [];
    private readonly TableLayoutPanel editorRoot = new();
    private readonly ContextMenuStrip rowMenu = new(), columnMenu = new();
    private readonly ContextMenuStrip secondaryRowMenu = new(), secondaryColumnMenu = new();
    private readonly ContextMenuStrip cellMenu = new();
    private ColumnFilterPopup? columnFilterPopup;
    private readonly Panel[] editOutline = [new(), new(), new(), new()];
    private bool fillDragging;
    private CellRange fillSource, fillPreview;
    private bool normalizingSelection;
    private int fillPrimaryRow, fillPrimaryColumn;
    private bool cellEditing;
    private int headerDropDownHoverColumn = -1, headerFilterColumn = -1;
    private HashSet<string>? headerFilterValues;
    private string? headerFilterOperator, headerFilterConditionValue;
    private readonly Font boldCellFont = new("Segoe UI", 9F, FontStyle.Bold);
    private readonly ToolStripStatusLabel positionLabel = new(), countLabel = new(), typeLabel = new(), dirtyLabel = new();
    private WorkbookModel workbook = WorkbookModel.CreateBlank();
    private SheetModel model;
    private readonly WorksheetPaneController primaryPane, secondaryPane;
    private Stack<IUndoStep> undo = new(), redo = new();
    private string? path;
    private bool dirty, loading;
    private string? filter;

    public MainForm(string? initialPath)
    {
        model = workbook.ActiveSheet.Sheet;
        primaryPane = new WorksheetPaneController(grid, new SheetModelDataSource(() => model), columnMenu, rowMenu, () => !loading);
        secondaryPane = new WorksheetPaneController(secondaryGrid, new SheetModelDataSource(() => secondaryModel ?? model), secondaryColumnMenu, secondaryRowMenu, () => !secondaryLoading);
        foreach (var pane in new[] { primaryPane, secondaryPane })
        {
            pane.RegularFont = Font; pane.BoldFont = boldCellFont;
        }
        primaryPane.CellCommitted += OnPrimaryCellCommitted;
        primaryPane.EditStarting += OnPrimaryEditStarting;
        primaryPane.EditFinished += OnPrimaryEditFinished;
        secondaryPane.CellCommitted += OnSecondaryCellCommitted;
        secondaryPane.EditStarting += OnSecondaryEditStarting;
        secondaryPane.EditFinished += OnSecondaryEditFinished;
        InitializeDocumentSessions();
        resizeCursorFilter = new ResizeCursorMessageFilter(this); Application.AddMessageFilter(resizeCursorFilter);
        Text = ""; AccessibleName = "SheetLite"; ShowIcon = false; Width = 1200; Height = 760; MinimumSize = new(720, 450); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.None; Padding = new Padding(1);
        BackColor = Theme.CurrentLine; ForeColor = Theme.Foreground; Font = new("Segoe UI", 9F);
        var appMenu = BuildMenu(); MainMenuStrip = appMenu; BuildToolbar(); BuildStatus(); ConfigureGrid(); BuildHeaderMenus(); BuildDockedWorkspace();
        editorRoot.Dock = DockStyle.Fill; editorRoot.Margin = Padding.Empty; editorRoot.Padding = Padding.Empty; editorRoot.ColumnCount = 1; editorRoot.RowCount = 3; editorRoot.BackColor = Theme.Background;
        editorRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editorRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); editorRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); editorRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        status.Dock = DockStyle.Fill; status.Margin = Padding.Empty; editorRoot.Controls.Add(commandHost, 0, 0); editorRoot.Controls.Add(workspace, 0, 1); editorRoot.Controls.Add(status, 0, 2);
        contentHost.Dock = DockStyle.Fill; contentHost.Margin = Padding.Empty; contentHost.BackColor = Theme.Background; contentHost.Controls.Add(editorRoot);
        welcome = BuildWelcome(); contentHost.Controls.Add(welcome); welcome.BringToFront(); ConfigureDragAndDrop();
        var chrome = BuildWindowChrome(appMenu);
        shell.Dock = DockStyle.Fill; shell.Margin = Padding.Empty; shell.Padding = Padding.Empty; shell.BackColor = Theme.Surface; shell.ColumnCount = 1; shell.RowCount = 2; shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.Controls.Add(chrome, 0, 0); shell.Controls.Add(contentHost, 0, 1); shell.SizeChanged += (_, _) => UpdateShellShape(); Controls.Add(shell);
        windowBorder.Bounds = ClientRectangle; windowBorder.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(windowBorder); windowBorder.BringToFront();
        KeyPreview = true; FormClosing += OnClosing;
        Shown += async (_, _) =>
        {
            NativeTheme.ApplyDarkWindow(this);
            UpdateWindowShape();
            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath)) OpenFile(initialPath);
            await CheckForUpdatesAsync(showCurrent: false);
        };
        ShowWelcome();
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = Theme.Surface, ForeColor = Theme.Foreground, AutoSize = false, Height = 39, ImageScalingSize = new Size(28, 28), Padding = new(0, 5, 0, 5), Margin = Padding.Empty, Renderer = new DraculaRenderer() };
        ToolStripMenuItem Item(string text, Keys shortcut, Action action)
        {
            var x = new ToolStripMenuItem(text)
            {
                // ProcessCmdKey is the single keyboard router. Keeping real menu-item
                // shortcuts as well can execute destructive commands twice.
                ShortcutKeys = Keys.None,
                ShortcutKeyDisplayString = shortcut == Keys.None ? "" : new KeysConverter().ConvertToString(shortcut)
            };
            x.Click += (_, _) => action();
            return x;
        }
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.AddRange([Item("New", Keys.Control | Keys.N, NewDocument), Item("Open…", Keys.Control | Keys.O, OpenDialog), Item("Open another…", Keys.Control | Keys.Shift | Keys.O, OpenAnotherDialog), Item("Save", Keys.Control | Keys.S, Save), Item("Save As…", Keys.Control | Keys.Shift | Keys.S, SaveAs), new ToolStripSeparator(), Item("Exit", Keys.Alt | Keys.F4, Close)]);
        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.AddRange([Item("Undo", Keys.Control | Keys.Z, Undo), Item("Redo", Keys.Control | Keys.Y, Redo), new ToolStripSeparator(), Item("Cut", Keys.Control | Keys.X, Cut), Item("Copy", Keys.Control | Keys.C, Copy), Item("Paste", Keys.Control | Keys.V, Paste), Item("Delete contents", Keys.Delete, DeleteContents), new ToolStripSeparator(), Item("Find…", Keys.Control | Keys.F, Find), Item("Replace…", Keys.Control | Keys.H, Replace)]);
        var rows = new ToolStripMenuItem("Grid");
        var rowCommands = new ToolStripMenuItem("Rows"); rowCommands.DropDownItems.AddRange([Item("Insert above", Keys.Control | Keys.Shift | Keys.Up, () => RunPrimaryCommand("Insert row above", InsertRow)), Item("Insert below", Keys.Control | Keys.Shift | Keys.Down, () => RunPrimaryCommand("Insert row below", InsertRowBelow)), Item("Delete selected rows", Keys.Control | Keys.Subtract, () => RunPrimaryCommand("Delete rows", DeleteRows)), Item("Move up", Keys.Alt | Keys.Up, () => RunPrimaryCommand("Move row up", () => MoveRow(-1))), Item("Move down", Keys.Alt | Keys.Down, () => RunPrimaryCommand("Move row down", () => MoveRow(1)))]);
        var columnCommands = new ToolStripMenuItem("Columns"); columnCommands.DropDownItems.AddRange([Item("Insert left", Keys.Control | Keys.Shift | Keys.Left, () => RunPrimaryCommand("Insert column left", InsertColumn)), Item("Insert right", Keys.Control | Keys.Shift | Keys.Right, () => RunPrimaryCommand("Insert column right", InsertColumnRight)), Item("Delete selected columns", Keys.Control | Keys.Shift | Keys.Subtract, () => RunPrimaryCommand("Delete columns", DeleteColumns)), Item("Move left", Keys.Alt | Keys.Left, () => RunPrimaryCommand("Move column left", () => MoveColumn(-1))), Item("Move right", Keys.Alt | Keys.Right, () => RunPrimaryCommand("Move column right", () => MoveColumn(1))), Item("Auto-size selected", Keys.Control | Keys.Alt | Keys.A, () => RunPrimaryCommand("Auto-size columns", AutoSizeColumns))]);
        var freezeCommands = new ToolStripMenuItem("Freeze panes"); freezeCommands.DropDownItems.AddRange([Item("Freeze at current cell", Keys.Control | Keys.Shift | Keys.F, () => RunPrimaryCommand("Freeze panes", Freeze)), Item("Unfreeze panes", Keys.None, () => RunPrimaryCommand("Unfreeze panes", Unfreeze))]);
        var formatCommands = new ToolStripMenuItem("Cell appearance"); formatCommands.DropDownItems.AddRange([Item("Background color…", Keys.None, SetBackground), Item("Text color…", Keys.None, SetForeground), Item("Reset colors to default", Keys.None, ResetColors), new ToolStripSeparator(), Item("Increase text size", Keys.None, () => ChangeFontSize(1)), Item("Decrease text size", Keys.None, () => ChangeFontSize(-1)), Item("Toggle bold", Keys.Control | Keys.B, ToggleBold), Item("Toggle italic", Keys.Control | Keys.I, ToggleItalic), Item("Toggle underline", Keys.Control | Keys.U, ToggleUnderline), new ToolStripSeparator(), Item("Align left", Keys.None, () => SetHorizontalAlignment(CellHorizontalAlignment.Left)), Item("Align center", Keys.None, () => SetHorizontalAlignment(CellHorizontalAlignment.Center)), Item("Align right", Keys.None, () => SetHorizontalAlignment(CellHorizontalAlignment.Right)), Item("Align top", Keys.None, () => SetVerticalAlignment(CellVerticalAlignment.Top)), Item("Align middle", Keys.None, () => SetVerticalAlignment(CellVerticalAlignment.Middle)), Item("Align bottom", Keys.None, () => SetVerticalAlignment(CellVerticalAlignment.Bottom)), new ToolStripSeparator(), Item("Clear formatting", Keys.Control | Keys.Shift | Keys.Space, ClearFormatting)]);
        rows.DropDownItems.AddRange([Item("Find…", Keys.Control | Keys.F, Find), Item("Filter…", Keys.Control | Keys.L, Filter), Item("Clear filter", Keys.Control | Keys.Shift | Keys.L, ClearFilter), Item("Sort…", Keys.Control | Keys.Shift | Keys.T, ShowSortPanel), new ToolStripSeparator(), rowCommands, columnCommands, freezeCommands, formatCommands]);
        var view = new ToolStripMenuItem("View");
        view.DropDownItems.AddRange([Item("SQL console", Keys.Control | Keys.Oem3, () => ToggleSqlConsole()), Item("Split view", Keys.Control | Keys.Alt | Keys.S, ToggleSplitView), new ToolStripSeparator(), Item("Open file in left pane…", Keys.None, () => OpenFileInSplitPane(primary: true)), Item("Open file in right pane…", Keys.None, () => OpenFileInSplitPane(primary: false))]);
        var help = new ToolStripMenuItem("Help"); help.DropDownItems.AddRange([Item("SheetLite Help", Keys.F1, () => ShowHelpPage()), Item("Keyboard shortcuts", Keys.None, () => ShowHelpPage("Keyboard shortcuts")), new ToolStripSeparator(), Item("Check for updates…", Keys.None, () => _ = CheckForUpdatesAsync(showCurrent: true)), Item("About SheetLite", Keys.None, About)]);
        var logo = new ToolStripLabel { Image = LoadBrandImage(), DisplayStyle = ToolStripItemDisplayStyle.Image, AutoSize = false, Size = new Size(42, 32), ImageScaling = ToolStripItemImageScaling.SizeToFit, Margin = Padding.Empty, Padding = new Padding(5, 1, 5, 1), AccessibleName = "SheetLite" };
        menu.Items.Add(logo); menu.Items.AddRange([file, edit, rows, view, help]);
        foreach (ToolStripMenuItem top in menu.Items.OfType<ToolStripMenuItem>()) { top.Margin = Padding.Empty; ConfigureDropDown(top); }
        return menu;
    }

    private Control BuildWindowChrome(MenuStrip menu)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 1) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty, Padding = Padding.Empty, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Surface, Margin = Padding.Empty, Padding = Padding.Empty };

        Button WindowButton(string resource, string accessibleName, Action action, bool close = false)
        {
            Image glyph = LoadWindowGlyph(resource); chromeGlyphs.Add(glyph);
            var button = new WindowChromeButton { AccessibleName = accessibleName, Image = glyph, ImageAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Foreground, Size = new Size(46, 39), Margin = Padding.Empty, Padding = Padding.Empty, TabStop = false, Cursor = Cursors.Hand, HoverColor = close ? Color.FromArgb(196, 43, 28) : Theme.Hover, PressedColor = close ? Color.FromArgb(178, 36, 24) : Theme.Selection };
            button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => action();
            return button;
        }

        controls.Controls.Add(WindowButton("window-minimize.png", "Minimize", () => WindowState = FormWindowState.Minimized));
        controls.Controls.Add(WindowButton("window-maximize.png", "Maximize or restore", ToggleMaximize));
        controls.Controls.Add(WindowButton("window-close.png", "Close", Close, close: true));
        menu.Dock = DockStyle.Fill;
        menu.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && menu.GetItemAt(e.Location) is null) BeginWindowDrag(); };
        menu.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left && menu.GetItemAt(e.Location) is null) ToggleMaximize(); };
        layout.Controls.Add(menu, 0, 0); layout.Controls.Add(controls, 1, 0);
        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.CurrentLine, Enabled = false };
        host.Controls.Add(layout); host.Controls.Add(divider); divider.BringToFront(); return host;
    }

    private static Image LoadWindowGlyph(string fileName)
    {
        using Stream? stream = typeof(MainForm).Assembly.GetManifestResourceStream($"SheetLite.Assets.{fileName}");
        if (stream is null) return new Bitmap(18, 18);
        using var source = new Bitmap(stream);
        int left = source.Width, top = source.Height, right = -1, bottom = -1;
        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
        {
            if (source.GetPixel(x, y).A <= 2) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top) return new Bitmap(18, 18);
        using var tinted = new Bitmap(right - left + 1, bottom - top + 1, PixelFormat.Format32bppPArgb);
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
        {
            Color pixel = source.GetPixel(x, y); tinted.SetPixel(x - left, y - top, Color.FromArgb(pixel.A, Theme.Foreground));
        }
        var result = new Bitmap(18, 18, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(result); graphics.Clear(Color.Transparent); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.PixelOffsetMode = PixelOffsetMode.HighQuality; graphics.DrawImage(tinted, new Rectangle(1, 1, 16, 16));
        return result;
    }

    private void BeginWindowDrag()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            Point pointer = Cursor.Position; Rectangle screen = Screen.FromPoint(pointer).WorkingArea; float horizontal = (pointer.X - screen.Left) / (float)Math.Max(1, screen.Width); WindowState = FormWindowState.Normal;
            Location = new Point(pointer.X - (int)(Width * Math.Clamp(horizontal, 0.15F, 0.85F)), pointer.Y - 12);
        }
        ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0);
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
        else { MaximizedBounds = Screen.FromHandle(Handle).WorkingArea; WindowState = FormWindowState.Maximized; }
        UpdateWindowShape();
    }

    private void UpdateWindowShape()
    {
        Region? old = Region;
        if (WindowState == FormWindowState.Maximized || Width < 20 || Height < 20) Region = null;
        else
        {
            int diameter = Math.Max(12, (int)Math.Round(12 * DeviceDpi / 96F));
            using var path = new GraphicsPath();
            path.AddArc(0, 0, diameter, diameter, 180, 90); path.AddArc(ClientSize.Width - diameter, 0, diameter, diameter, 270, 90); path.AddArc(ClientSize.Width - diameter, ClientSize.Height - diameter, diameter, diameter, 0, 90); path.AddArc(0, ClientSize.Height - diameter, diameter, diameter, 90, 90); path.CloseFigure();
            Region = new Region(path);
        }
        if (!ReferenceEquals(old, Region)) old?.Dispose();
        UpdateShellShape();
        windowBorder.RefreshShape();
    }

    private void UpdateShellShape()
    {
        if (shell.IsDisposed) return;
        Region? old = shell.Region;
        if (WindowState == FormWindowState.Maximized || shell.Width < 18 || shell.Height < 18) shell.Region = null;
        else
        {
            int diameter = Math.Max(10, (int)Math.Round(10 * DeviceDpi / 96F));
            using var path = new GraphicsPath();
            path.AddArc(0, 0, diameter, diameter, 180, 90); path.AddArc(shell.ClientSize.Width - diameter, 0, diameter, diameter, 270, 90); path.AddArc(shell.ClientSize.Width - diameter, shell.ClientSize.Height - diameter, diameter, diameter, 0, 90); path.AddArc(0, shell.ClientSize.Height - diameter, diameter, diameter, 90, 90); path.CloseFigure();
            shell.Region = new Region(path);
        }
        if (!ReferenceEquals(old, shell.Region)) old?.Dispose();
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateWindowShape(); }

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsCaption = 0x00C00000, wsThickFrame = 0x00040000, wsMinimizeBox = 0x00020000, wsMaximizeBox = 0x00010000, wsSysMenu = 0x00080000;
            const int wsExDlgModalFrame = 0x00000001, wsExWindowEdge = 0x00000100, wsExClientEdge = 0x00000200, wsExStaticEdge = 0x00020000;
            CreateParams parameters = base.CreateParams;
            parameters.Style &= ~(wsCaption | wsThickFrame);
            parameters.Style |= wsMinimizeBox | wsMaximizeBox | wsSysMenu;
            parameters.ExStyle &= ~(wsExDlgModalFrame | wsExWindowEdge | wsExClientEdge | wsExStaticEdge);
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StripNativeBorderStyles();
        NativeTheme.ApplyWindowChrome(this);
        UpdateWindowShape();
    }

    private void StripNativeBorderStyles()
    {
        const int gwlStyle = -16, gwlExStyle = -20;
        const long wsCaption = 0x00C00000, wsExDlgModalFrame = 0x00000001, wsExWindowEdge = 0x00000100, wsExClientEdge = 0x00000200, wsExStaticEdge = 0x00020000;
        const uint swpNoSize = 0x0001, swpNoMove = 0x0002, swpNoZOrder = 0x0004, swpNoActivate = 0x0010, swpFrameChanged = 0x0020;
        long style = GetWindowLongPtr(Handle, gwlStyle).ToInt64(), exStyle = GetWindowLongPtr(Handle, gwlExStyle).ToInt64();
        long cleanStyle = style & ~wsCaption, cleanExStyle = exStyle & ~(wsExDlgModalFrame | wsExWindowEdge | wsExClientEdge | wsExStaticEdge);
        if (cleanStyle == style && cleanExStyle == exStyle) return;
        SetWindowLongPtr(Handle, gwlStyle, (IntPtr)cleanStyle); SetWindowLongPtr(Handle, gwlExStyle, (IntPtr)cleanExStyle);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, swpNoSize | swpNoMove | swpNoZOrder | swpNoActivate | swpFrameChanged);
    }

    protected override void WndProc(ref Message message)
    {
        const int wmSetCursor = 0x20, wmNcCalcSize = 0x83, wmNcHitTest = 0x84, wmNcLeftButtonDown = 0xA1, wmSysCommand = 0x112, scSize = 0xF000, grip = 7;
        if (message.Msg == wmSetCursor && WindowState == FormWindowState.Normal)
        {
            int hit = ResizeHitAt(PointToClient(Cursor.Position), grip);
            Cursor? resizeCursor = ResizeCursorForHit(hit);
            if (resizeCursor is not null) { Cursor.Current = resizeCursor; message.Result = (IntPtr)1; return; }
        }
        // Windows uses both NCCALCSIZE_PARAMS (wParam != 0) and RECT (wParam == 0)
        // forms while resizing or rebuilding the handle. Letting either reach the
        // default procedure can restore the native caption over our custom chrome.
        if (message.Msg == wmNcCalcSize) { message.Result = IntPtr.Zero; return; }
        if (message.Msg == wmNcLeftButtonDown && WindowState == FormWindowState.Normal)
        {
            int hit = message.WParam.ToInt32();
            if (hit is >= 10 and <= 17)
            {
                ReleaseCapture(); PostMessage(Handle, wmSysCommand, scSize | (hit - 9), 0);
                message.Result = IntPtr.Zero; return;
            }
        }
        if (message.Msg == wmNcHitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref message); Point point = PointToClient(Cursor.Position);
            int hit = ResizeHitAt(point, grip);
            if (hit != 0)
            {
                Cursor.Current = ResizeCursorForHit(hit)!;
                message.Result = (IntPtr)hit;
            }
            return;
        }
        base.WndProc(ref message);
    }

    private int ResizeHitAt(Point point, int grip)
    {
        bool left = point.X <= grip, right = point.X >= ClientSize.Width - grip, top = point.Y <= grip, bottom = point.Y >= ClientSize.Height - grip;
        return top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
    }

    private static Cursor? ResizeCursorForHit(int hit) => hit switch
    {
        10 or 11 => Cursors.SizeWE, 12 or 15 => Cursors.SizeNS, 13 or 17 => Cursors.SizeNWSE, 14 or 16 => Cursors.SizeNESW, _ => null
    };

    internal Cursor? ResizeCursorAtScreen(Point screenPoint) => WindowState == FormWindowState.Normal ? ResizeCursorForHit(ResizeHitAt(PointToClient(screenPoint), 7)) : null;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, int message, int wParam, int lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private static void ConfigureDropDown(ToolStripMenuItem item)
    {
        if (item.DropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false; menu.ShowCheckMargin = false; menu.Padding = Padding.Empty;
            menu.BackColor = Theme.Background; menu.ForeColor = Theme.Foreground; menu.Renderer = new DraculaRenderer();
        }
        foreach (ToolStripMenuItem child in item.DropDownItems.OfType<ToolStripMenuItem>()) ConfigureDropDown(child);
    }

    private ToolStrip BuildToolbar()
    {
        toolbar.Dock = DockStyle.Right; toolbar.Width = 276; toolbar.Height = 34; toolbar.AutoSize = false; toolbar.CanOverflow = false; toolbar.LayoutStyle = ToolStripLayoutStyle.Flow; toolbar.GripStyle = ToolStripGripStyle.Hidden; toolbar.BackColor = Theme.Surface; toolbar.ForeColor = Theme.Foreground; toolbar.Padding = new Padding(4, 0, 4, 0); toolbar.Margin = Padding.Empty; toolbar.Renderer = new DraculaRenderer(); toolbar.AccessibleName = "Worksheet toolbar";
        if (toolbar.LayoutSettings is FlowLayoutSettings flow) { flow.FlowDirection = FlowDirection.LeftToRight; flow.WrapContents = true; }
        ToolStripButton Add(UiIcon icon, string tip, Action action)
        {
            var b = new ToolStripButton { Alignment = ToolStripItemAlignment.Left, Image = UiIcons.Draw(icon, Theme.Foreground), ToolTipText = tip, AccessibleName = tip, DisplayStyle = ToolStripItemDisplayStyle.Image, AutoSize = false, Size = new Size(28, 30), ImageScaling = ToolStripItemImageScaling.None, Margin = Padding.Empty, Padding = Padding.Empty, Overflow = ToolStripItemOverflow.Never };
            b.Click += (_, _) => action(); toolbar.Items.Add(b); return b;
        }
        void Divider() => toolbar.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(8, 24), Margin = new Padding(1, 5, 1, 5) });
        Add(UiIcon.Search, "Find and replace (Ctrl+F / Ctrl+H)", () => RunPrimaryCommand("Find and replace", Find));
        Add(UiIcon.Sort, "Sort rows (Ctrl+Shift+T)", () => RunPrimaryCommand("Sort", ShowSortPanel));
        Add(UiIcon.Filter, "Filter rows (Ctrl+L)", () => RunPrimaryCommand("Filter", Filter));
        Divider();
        Add(UiIcon.Database, "SQL console (Ctrl+`)", () => RunPrimaryCommand("SQL", () => ToggleSqlConsole()));
        Add(UiIcon.Split, "Split view (Ctrl+Alt+S)", ToggleSplitView);
        Divider();
        Add(UiIcon.Freeze, "Freeze panes at the current selection (Ctrl+Shift+F)", () => RunPrimaryCommand("Freeze panes", Freeze));
        Add(UiIcon.Fill, "Cell background color", SetBackground);
        Add(UiIcon.TextColor, "Cell text color", SetForeground);
        Divider();
        Add(UiIcon.FontSizeDown, "Decrease text size", () => ChangeFontSize(-1));
        Add(UiIcon.FontSizeUp, "Increase text size", () => ChangeFontSize(1));
        boldButton = Add(UiIcon.Bold, "Bold (Ctrl+B)", ToggleBold);
        italicButton = Add(UiIcon.Italic, "Italic (Ctrl+I)", ToggleItalic);
        underlineButton = Add(UiIcon.Underline, "Underline (Ctrl+U)", ToggleUnderline);
        Divider();
        alignLeftButton = Add(UiIcon.AlignLeft, "Align text left", () => SetHorizontalAlignment(CellHorizontalAlignment.Left));
        alignCenterButton = Add(UiIcon.AlignCenter, "Align text center", () => SetHorizontalAlignment(CellHorizontalAlignment.Center));
        alignRightButton = Add(UiIcon.AlignRight, "Align text right", () => SetHorizontalAlignment(CellHorizontalAlignment.Right));
        Divider();
        alignTopButton = Add(UiIcon.AlignTop, "Align text top", () => SetVerticalAlignment(CellVerticalAlignment.Top));
        alignMiddleButton = Add(UiIcon.AlignMiddle, "Align text middle", () => SetVerticalAlignment(CellVerticalAlignment.Middle));
        alignBottomButton = Add(UiIcon.AlignBottom, "Align text bottom", () => SetVerticalAlignment(CellVerticalAlignment.Bottom));
        return toolbar;
    }

    private void RunPrimaryCommand(string name, Action action)
    {
        if (secondaryPaneActive && !secondarySharesPrimary) { ShowNotice("Left-pane command", $"{name} currently targets the left document. Select its document tab first."); return; }
        if (secondaryPaneActive) MirrorSecondarySelectionToPrimary();
        action();
    }

    private StatusStrip BuildStatus()
    {
        status.BackColor = Theme.Surface; status.ForeColor = Theme.Foreground; status.Renderer = new DraculaRenderer(); status.SizingGrip = false;
        positionLabel.Spring = false; countLabel.Spring = true; countLabel.TextAlign = ContentAlignment.MiddleRight;
        status.Items.AddRange([positionLabel, new ToolStripStatusLabel("  |  "), typeLabel, countLabel, dirtyLabel]); return status;
    }

    private Panel BuildWelcome()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        var card = new TableLayoutPanel { Width = 680, Height = 390, ColumnCount = 2, RowCount = 3, BackColor = page.BackColor, Anchor = AnchorStyles.None };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 105)); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var brand = new Panel { Dock = DockStyle.Fill, BackColor = page.BackColor, Padding = new Padding(8, 18, 0, 4) };
        var brandIcon = new PictureBox { Image = LoadBrandImage(), SizeMode = PictureBoxSizeMode.Zoom, Width = 58, Dock = DockStyle.Left, BackColor = Color.Transparent, Margin = Padding.Empty };
        var brandName = new Label { Text = "SheetLite", Font = new Font("Segoe UI", 20, FontStyle.Regular), ForeColor = Theme.Purple, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
        brand.Controls.Add(brandName); brand.Controls.Add(brandIcon);
        card.Controls.Add(brand, 0, 0); card.SetColumnSpan(brand, 2);
        card.Controls.Add(new Label { Text = "Start", ForeColor = Theme.Comment, Dock = DockStyle.Fill, Padding = new Padding(10, 5, 0, 0) }, 0, 1);
        card.Controls.Add(new Label { Text = "Recent this session", ForeColor = Theme.Comment, Dock = DockStyle.Fill, Padding = new Padding(18, 5, 0, 0) }, 1, 1);
        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Padding = new Padding(8, 4, 35, 0), BackColor = page.BackColor };
        actions.Controls.Add(WelcomeButton("＋  New spreadsheet", "Ctrl + N", NewDocument));
        actions.Controls.Add(WelcomeButton("▱  Open CSV or XLSX…", "Ctrl + O", OpenDialog));
        var line = new Panel { Height = 1, Width = 320, BackColor = Theme.CurrentLine, Margin = new Padding(0, 10, 0, 10) }; actions.Controls.Add(line);
        actions.Controls.Add(WelcomeButton("?  Help and feature guide", "F1", () => ShowHelpPage()));
        actions.Controls.Add(WelcomeButton("⌨  Keyboard shortcuts", "", () => ShowHelpPage("Keyboard shortcuts")));
        card.Controls.Add(actions, 0, 2);
        recentFiles.FlowDirection = FlowDirection.TopDown; recentFiles.WrapContents = false; recentFiles.Dock = DockStyle.Fill; recentFiles.Padding = new Padding(16, 4, 5, 0); recentFiles.BackColor = page.BackColor;
        card.Controls.Add(recentFiles, 1, 2);
        page.Controls.Add(card);
        page.Resize += (_, _) =>
        {
            card.Width = Math.Clamp(page.ClientSize.Width - 40, 640, 780);
            card.Location = new Point(Math.Max(20, (page.ClientSize.Width - card.Width) / 2), Math.Max(20, (page.ClientSize.Height - card.Height) / 2));
        };
        return page;
    }

    private static Image? LoadBrandImage()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("SheetLite.Assets.sheetlite-icon.png");
        if (stream is null) return null;
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private Button WelcomeButton(string text, string shortcut, Action action)
    {
        var button = new Button { Text = text, Width = 320, Height = 38, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface, ForeColor = Theme.Foreground, Font = new Font("Segoe UI", 10), Margin = Padding.Empty, Padding = new Padding(4, 0, 0, 0), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = Theme.CurrentLine; button.FlatAppearance.MouseDownBackColor = Theme.Comment;
        button.Paint += (_, e) => { using var shortcutFont = new Font("Consolas", 8); TextRenderer.DrawText(e.Graphics, shortcut, shortcutFont, new Rectangle(button.Width - 88, 0, 82, button.Height), Theme.Comment, TextFormatFlags.Right | TextFormatFlags.VerticalCenter); };
        button.Click += (_, _) => action(); return button;
    }

    private void ShowWelcome()
    {
        editorRoot.Visible = false; welcome.Visible = true; welcome.BringToFront(); Text = ""; RefreshRecentFiles();
    }

    private void ShowEditor()
    {
        welcome.Visible = false; helpPage.Visible = false; editorRoot.Visible = true; editorRoot.BringToFront();
    }

    private void RefreshRecentFiles()
    {
        recentFiles.Controls.Clear();
        if (sessionRecent.Count == 0) { recentFiles.Controls.Add(new Label { Text = "Files you open will appear here.\nNothing is stored after SheetLite closes.", AutoSize = true, ForeColor = Theme.Comment, Padding = new Padding(4, 7, 0, 0) }); return; }
        foreach (string file in sessionRecent.Take(8))
        {
            var button = WelcomeButton("▤  " + Path.GetFileName(file), "", () => OpenFile(file)); button.Width = 345; button.Tag = file;
            recentToolTip.SetToolTip(button, file); recentFiles.Controls.Add(button);
        }
    }

    private void ConfigureGrid()
    {
        primaryPane.RegularFont = Font; primaryPane.BoldFont = boldCellFont;
        secondaryPane.RegularFont = Font; secondaryPane.BoldFont = boldCellFont;
        grid.Dock = DockStyle.Fill; grid.BackgroundColor = Theme.CellBackground; grid.GridColor = Theme.CurrentLine; grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersDefaultCellStyle = new() { BackColor = Theme.HeaderBackground, ForeColor = Theme.Foreground, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Purple, Alignment = DataGridViewContentAlignment.MiddleCenter };
        grid.RowHeadersDefaultCellStyle = new() { BackColor = Theme.HeaderBackground, ForeColor = Theme.Comment, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Purple, Alignment = DataGridViewContentAlignment.MiddleRight };
        grid.DefaultCellStyle = new() { BackColor = Theme.CellBackground, ForeColor = Theme.Foreground, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Foreground, NullValue = "", Font = Font };
        grid.RowTemplate.Height = 23; grid.ColumnHeadersHeight = 25; grid.RowHeadersWidth = 58; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText; grid.SelectionMode = DataGridViewSelectionMode.CellSelect; grid.MultiSelect = true;
        grid.CellValueChanged += (_, e) => { if (!loading && !cellEditing && e.RowIndex >= 0) SetDirtyCell(e.RowIndex, e.ColumnIndex); };
        grid.CurrentCellChanged += (_, _) => { if (!cellEditing) HideEditOutline(); UpdateStatus(); UpdateToolbarFormattingState(); grid.Invalidate(); }; grid.SelectionChanged += (_, _) => { NormalizeDragSelection(); UpdateStatus(); UpdateToolbarFormattingState(); grid.Invalidate(); };
        grid.Enter += (_, _) => SetActivePane(false); grid.MouseDown += (_, _) => SetActivePane(false);
        grid.CellPainting += PaintHeader;
        grid.CellPainting += PaintSelection;
        grid.CellPainting += PaintPaneDimmer;
        grid.CellMouseMove += TrackHeaderDropDown; grid.MouseLeave += (_, _) => SetHeaderDropDownHover(-1);
        grid.CellMouseDown += SelectHeaderRange;
        grid.CellMouseUp += ShowHeaderFilterDropDown;
        grid.CellMouseDown += ShowCellMenu;
        grid.MouseDown += BeginFillDrag; grid.MouseMove += ContinueFillDrag; grid.MouseUp += EndFillDrag; grid.Paint += PaintFillPreview;
        grid.ColumnWidthChanged += (_, _) => UpdateEditOutline();
        grid.CellParsing += (_, e) => { if (e.Value is not null) { e.Value = e.Value.ToString(); e.ParsingApplied = true; } };
        grid.EditingControlShowing += (_, e) => { cellEditing = true; if (e.Control is TextBox box) { box.ContextMenuStrip = cellMenu; box.BorderStyle = BorderStyle.None; if (grid.CurrentCell is { } cell) { CellDisplayValue display = primaryPane.Source.GetDisplayValue(new(ModelRow(cell.RowIndex), cell.ColumnIndex)); box.BackColor = display.BackColor; box.ForeColor = display.ForeColor; } } BeginInvoke(() => { UpdateEditOutline(); grid.Invalidate(); }); };
        scrollCorner.BackColor = Theme.Hover; scrollCorner.Size = new(SystemInformation.VerticalScrollBarWidth, SystemInformation.HorizontalScrollBarHeight); scrollCorner.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; scrollCorner.Enabled = false;
        grid.Controls.Add(scrollCorner); grid.Resize += (_, _) => PositionScrollCorner(); grid.HandleCreated += (_, _) => PositionScrollCorner();
        foreach (var edge in editOutline) { edge.BackColor = Theme.Purple; edge.Visible = false; edge.Enabled = false; edge.TabStop = false; grid.Controls.Add(edge); }
        grid.Scroll += (_, _) => UpdateEditOutline();
    }

    private void BuildHeaderMenus()
    {
        BuildRowHeaderMenu(); BuildColumnHeaderMenu();
        BuildSecondaryRowHeaderMenu(); BuildSecondaryColumnHeaderMenu();

        ConfigureContextMenu(cellMenu);
        AddContextItem(cellMenu, "Cut", Cut, "Ctrl+X");
        AddContextItem(cellMenu, "Copy", Copy, "Ctrl+C");
        AddContextItem(cellMenu, "Paste", Paste, "Ctrl+V");
        cellMenu.Items.Add(new ToolStripSeparator());
        AddContextItem(cellMenu, "Clear contents", DeleteContents, "Delete");
        cellMenu.Items.Add(new ToolStripSeparator());
        AddContextItem(cellMenu, "Insert row above", InsertRow, "Ctrl+Shift+↑");
        AddContextItem(cellMenu, "Insert row below", InsertRowBelow, "Ctrl+Shift+↓");
        AddContextItem(cellMenu, "Insert column left", InsertColumn, "Ctrl+Shift+←");
        AddContextItem(cellMenu, "Insert column right", InsertColumnRight, "Ctrl+Shift+→");
        cellMenu.Items.Add(new ToolStripSeparator());
        AddContextItem(cellMenu, "Sort ascending", () => Sort(true));
        AddContextItem(cellMenu, "Sort descending", () => Sort(false));
        AddContextItem(cellMenu, "Filter by value…", FilterByCurrentValue);
        cellMenu.Items.Add(new ToolStripSeparator());
        AddContextItem(cellMenu, "Cell background…", SetBackground);
        AddContextItem(cellMenu, "Text color…", SetForeground);
        AddContextItem(cellMenu, "Reset colors to default", ResetColors);
        cellMenu.Items.Add(new ToolStripSeparator());
        AddContextItem(cellMenu, "Toggle bold", ToggleBold, "Ctrl+B");
        AddContextItem(cellMenu, "Toggle italic", ToggleItalic, "Ctrl+I");
        AddContextItem(cellMenu, "Toggle underline", ToggleUnderline, "Ctrl+U");
    }

    private static void ConfigureContextMenu(ContextMenuStrip menu)
    {
        menu.ShowImageMargin = false; menu.ShowCheckMargin = false; menu.Padding = Padding.Empty;
        menu.BackColor = Theme.Background; menu.ForeColor = Theme.Foreground; menu.Renderer = new DraculaRenderer();
    }

    private static void AddContextItem(ContextMenuStrip menu, string text, Action action, string shortcut = "")
    {
        var item = new ToolStripMenuItem(text) { ForeColor = Theme.Foreground, BackColor = Theme.Background, ShortcutKeyDisplayString = shortcut };
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }

    private void PositionScrollCorner()
    {
        PositionScrollCorner(grid, scrollCorner);
    }

    private static void PositionScrollCorner(DataGridView owner, Panel corner)
    {
        corner.Location = new Point(owner.ClientSize.Width - corner.Width, owner.ClientSize.Height - corner.Height);
        corner.BringToFront();
    }

    private void UpdateEditOutline()
    {
        if (!grid.IsCurrentCellInEditMode || grid.CurrentCell is null) { HideEditOutline(); return; }
        Rectangle cell = grid.GetCellDisplayRectangle(grid.CurrentCell.ColumnIndex, grid.CurrentCell.RowIndex, false);
        if (cell.Width <= 0 || cell.Height <= 0) { HideEditOutline(); return; }
        const int thickness = 1;
        editOutline[0].Bounds = new(cell.Left, cell.Top, cell.Width, thickness);
        editOutline[1].Bounds = new(cell.Left, cell.Bottom - thickness, cell.Width, thickness);
        editOutline[2].Bounds = new(cell.Left, cell.Top, thickness, cell.Height);
        editOutline[3].Bounds = new(cell.Right - thickness, cell.Top, thickness, cell.Height);
        foreach (var edge in editOutline) { edge.Visible = true; edge.BringToFront(); }
    }

    private void HideEditOutline() { foreach (var edge in editOutline) edge.Visible = false; }

    private bool OnPrimaryEditStarting(int row, int column)
    {
        if (loading) return false;
        PushUndo();
        cellEditing = true; grid.Invalidate(); BeginInvoke(() => { UpdateEditOutline(); grid.Invalidate(); });
        return true;
    }

    private void OnPrimaryEditFinished(int row, int column)
    {
        cellEditing = false; HideEditOutline(); UpdateStatus();
    }

    private void OnPrimaryCellCommitted(int row, int column)
    {
        if (model.IsFormula(row, column)) { var result = FormulaEngine.Evaluate(model, row, column); typeLabel.Text = result.Success ? "Formula" : result.Error ?? "Formula error"; }
        ReapplyDockedFilterIfActive(); SetDirtyCell(row, column);
    }

    private void SelectHeaderRange(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
        bool clickedSelection = false;
        if (e.RowIndex >= 0 && e.ColumnIndex == -1)
        {
            clickedSelection = true; foreach (DataGridViewCell cell in grid.Rows[e.RowIndex].Cells) if (!cell.Selected) { clickedSelection = false; break; }
        }
        else if (e.RowIndex == -1 && e.ColumnIndex >= 0)
        {
            clickedSelection = true; foreach (DataGridViewRow row in grid.Rows) if (!grid[e.ColumnIndex, row.Index].Selected) { clickedSelection = false; break; }
        }
        if (e.Button == MouseButtons.Left)
        {
            bool control = ModifierKeys.HasFlag(Keys.Control), shift = ModifierKeys.HasFlag(Keys.Shift);
            if (!control && !shift) grid.ClearSelection();
            if (e.RowIndex == -1 && e.ColumnIndex == -1) foreach (DataGridViewCell cell in grid.Rows.Cast<DataGridViewRow>().SelectMany(r => r.Cells.Cast<DataGridViewCell>())) cell.Selected = true;
            else if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                int anchor = shift && grid.CurrentCell is not null ? grid.CurrentCell.ColumnIndex : e.ColumnIndex;
                bool select = !control || !clickedSelection;
                for (int column = Math.Min(anchor, e.ColumnIndex); column <= Math.Max(anchor, e.ColumnIndex); column++) foreach (DataGridViewRow row in grid.Rows) grid[column, row.Index].Selected = select;
                if (select) grid.CurrentCell = grid[e.ColumnIndex, 0];
            }
            else if (e.ColumnIndex == -1 && e.RowIndex >= 0)
            {
                int anchor = shift && grid.CurrentCell is not null ? grid.CurrentCell.RowIndex : e.RowIndex;
                bool select = !control || !clickedSelection;
                for (int row = Math.Min(anchor, e.RowIndex); row <= Math.Max(anchor, e.RowIndex); row++) foreach (DataGridViewCell cell in grid.Rows[row].Cells) cell.Selected = select;
                if (select) grid.CurrentCell = grid[0, e.RowIndex];
            }
        }
        else
        {
            if (!clickedSelection)
            {
                grid.ClearSelection();
                if (e.ColumnIndex == -1 && e.RowIndex >= 0) { foreach (DataGridViewCell cell in grid.Rows[e.RowIndex].Cells) cell.Selected = true; grid.CurrentCell = grid[0, e.RowIndex]; }
                else if (e.RowIndex == -1 && e.ColumnIndex >= 0) { foreach (DataGridViewRow row in grid.Rows) grid[e.ColumnIndex, row.Index].Selected = true; grid.CurrentCell = grid[e.ColumnIndex, 0]; }
            }
        }
    }

    private void PaintHeader(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if ((e.RowIndex >= 0 && e.ColumnIndex >= 0) || e.Graphics is null) return;
        bool selected = e.State.HasFlag(DataGridViewElementStates.Selected); using var background = new SolidBrush(selected ? Theme.Selection : Theme.HeaderBackground);
        e.Graphics.FillRectangle(background, e.CellBounds);
        using var border = new Pen(Theme.CurrentLine);
        e.Graphics.DrawLine(border, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom);
        e.Graphics.DrawLine(border, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        string text = e.RowIndex == -1 && e.ColumnIndex >= 0 ? grid.Columns[e.ColumnIndex].HeaderText : e.ColumnIndex == -1 && e.RowIndex >= 0 ? (ModelRow(e.RowIndex) + 1).ToString() : "";
        Color color = selected ? Theme.Purple : e.RowIndex == -1 ? Theme.Foreground : Theme.Comment;
        var flags = TextFormatFlags.VerticalCenter | (e.ColumnIndex == -1 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter) | TextFormatFlags.EndEllipsis;
        var bounds = Rectangle.Inflate(e.CellBounds, -6, 0);
        TextRenderer.DrawText(e.Graphics, text, e.CellStyle?.Font ?? Font, bounds, color, flags);
        if (e.RowIndex == -1 && e.ColumnIndex >= 0) PaintHeaderDropDown(e.Graphics, e.CellBounds, e.ColumnIndex);
        e.Handled = true;
    }

    private void PaintSelection(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || !grid[e.ColumnIndex, e.RowIndex].Selected || e.Graphics is null) return;
        e.Paint(e.CellBounds, e.PaintParts);
        bool primary = grid.CurrentCell?.RowIndex == e.RowIndex && grid.CurrentCell.ColumnIndex == e.ColumnIndex;
        if (!primary || cellEditing) { e.Handled = true; return; }
        var bounds = Rectangle.Inflate(e.CellBounds, -1, -1);
        using var pen = new Pen(Theme.Purple, 2F);
        e.Graphics.DrawRectangle(pen, bounds);
        e.Handled = true;
    }

    private void PaintPaneDimmer(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView target || !IsPaneInactive(target) || e.Graphics is null) return;
        if (!e.Handled) e.Paint(e.CellBounds, e.PaintParts);
        using var shade = new SolidBrush(Color.FromArgb(42, Color.Black));
        e.Graphics.FillRectangle(shade, e.CellBounds);
        e.Handled = true;
    }

    private bool IsPaneInactive(DataGridView target) => !splitView.Panel2Collapsed &&
        (ReferenceEquals(target, grid) ? secondaryPaneActive : !secondaryPaneActive);

    private void ShowCellMenu(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (!grid[e.ColumnIndex, e.RowIndex].Selected) { grid.ClearSelection(); grid[e.ColumnIndex, e.RowIndex].Selected = true; grid.CurrentCell = grid[e.ColumnIndex, e.RowIndex]; }
        cellMenu.Show(Cursor.Position);
    }

    private Rectangle FillHandleGrabArea()
    {
        if (!TryGetFillRange(out var range)) return Rectangle.Empty;
        Rectangle cell = grid.GetCellDisplayRectangle(range.Right, range.Bottom, false);
        return new Rectangle(cell.Right - 11, cell.Bottom - 11, 18, 18);
    }

    private void BeginFillDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !FillHandleGrabArea().Contains(e.Location) || !TryGetFillRange(out fillSource)) return;
        fillPrimaryRow = grid.CurrentCell?.RowIndex ?? fillSource.Top; fillPrimaryColumn = grid.CurrentCell?.ColumnIndex ?? fillSource.Left;
        fillPreview = fillSource; fillDragging = true; grid.Capture = true; grid.Cursor = Cursors.Cross; grid.Invalidate();
    }

    private void ContinueFillDrag(object? sender, MouseEventArgs e)
    {
        if (!fillDragging) { grid.Cursor = FillHandleGrabArea().Contains(e.Location) ? Cursors.Cross : Cursors.Default; return; }
        RestoreFillSourceSelection();
        var hit = grid.HitTest(e.X, e.Y); if (hit.RowIndex < 0 || hit.ColumnIndex < 0) return;
        int verticalDistance = hit.RowIndex < fillSource.Top ? fillSource.Top - hit.RowIndex : Math.Max(0, hit.RowIndex - fillSource.Bottom);
        int horizontalDistance = hit.ColumnIndex < fillSource.Left ? fillSource.Left - hit.ColumnIndex : Math.Max(0, hit.ColumnIndex - fillSource.Right);
        fillPreview = verticalDistance >= horizontalDistance
            ? new CellRange(fillSource.Left, Math.Min(fillSource.Top, hit.RowIndex), fillSource.Right, Math.Max(fillSource.Bottom, hit.RowIndex))
            : new CellRange(Math.Min(fillSource.Left, hit.ColumnIndex), fillSource.Top, Math.Max(fillSource.Right, hit.ColumnIndex), fillSource.Bottom);
        grid.Invalidate();
    }

    private void EndFillDrag(object? sender, MouseEventArgs e)
    {
        if (!fillDragging) return; fillDragging = false; grid.Capture = false; grid.Cursor = Cursors.Default;
        if (fillPreview != fillSource) ApplyFill(fillSource, fillPreview); grid.Invalidate();
    }

    private void PaintFillPreview(object? sender, PaintEventArgs e)
    {
        if ((splitView.Panel2Collapsed || !secondaryPaneActive) && !cellEditing && TryGetFillRange(out var selection))
        {
            Rectangle selectionBounds = CellRangeDisplayRectangle(selection);
            if (grid.SelectedCells.Count > 1 && !selectionBounds.IsEmpty)
            {
                using var selectionPen = new Pen(Theme.Purple, 1.5F) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                e.Graphics.DrawRectangle(selectionPen, Rectangle.Inflate(selectionBounds, -1, -1));
            }
            Rectangle cell = grid.GetCellDisplayRectangle(selection.Right, selection.Bottom, false);
            Rectangle handle = new(cell.Right - 5, cell.Bottom - 5, 9, 9);
            using var handleFill = new SolidBrush(Theme.Purple); using var handleOutline = new Pen(Theme.CellBackground, 2);
            e.Graphics.FillEllipse(handleFill, handle); e.Graphics.DrawEllipse(handleOutline, handle);
        }
        if (!fillDragging) return; Rectangle bounds = CellRangeDisplayRectangle(fillPreview); if (bounds.IsEmpty) return;
        using var pen = new Pen(Theme.Purple, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        e.Graphics.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1));
        using var font = new Font("Segoe UI", 12, FontStyle.Bold); TextRenderer.DrawText(e.Graphics, "+", font, new Rectangle(bounds.Right - 22, bounds.Bottom - 24, 20, 20), Theme.Purple, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private CellRange SelectionRange()
    {
        if (grid.SelectedCells.Count == 0 && grid.CurrentCell is not null) return new(grid.CurrentCell.ColumnIndex, grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex, grid.CurrentCell.RowIndex);
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        foreach (DataGridViewCell cell in grid.SelectedCells) { left = Math.Min(left, cell.ColumnIndex); top = Math.Min(top, cell.RowIndex); right = Math.Max(right, cell.ColumnIndex); bottom = Math.Max(bottom, cell.RowIndex); }
        return right < 0 ? default : new(left, top, right, bottom);
    }

    private bool TryGetFillRange(out CellRange range)
    {
        range = default; if (grid.CurrentCell is null || grid.SelectedCells.Count == 0) return false;
        range = SelectionRange();
        bool rectangular = grid.SelectedCells.Count == range.Width * range.Height;
        bool wholeHeaderRange = range.Width == grid.ColumnCount || range.Height == grid.RowCount;
        return rectangular && !wholeHeaderRange;
    }

    private void NormalizeDragSelection()
    {
        if (normalizingSelection || fillDragging || MouseButtons != MouseButtons.Left || ModifierKeys.HasFlag(Keys.Control) || grid.SelectedCells.Count < 2) return;
        var range = SelectionRange(); if (grid.SelectedCells.Count == range.Width * range.Height) return;
        normalizingSelection = true;
        try { for (int r = range.Top; r <= range.Bottom; r++) for (int c = range.Left; c <= range.Right; c++) grid[c, r].Selected = true; }
        finally { normalizingSelection = false; }
    }

    private void RestoreFillSourceSelection()
    {
        bool exact = grid.SelectedCells.Count == fillSource.Width * fillSource.Height;
        if (exact) foreach (DataGridViewCell cell in grid.SelectedCells) if (cell.RowIndex < fillSource.Top || cell.RowIndex > fillSource.Bottom || cell.ColumnIndex < fillSource.Left || cell.ColumnIndex > fillSource.Right) { exact = false; break; }
        if (exact) return;
        normalizingSelection = true;
        try
        {
            grid.ClearSelection();
            for (int r = fillSource.Top; r <= fillSource.Bottom; r++) for (int c = fillSource.Left; c <= fillSource.Right; c++) grid[c, r].Selected = true;
            grid.CurrentCell = grid[fillPrimaryColumn, fillPrimaryRow];
        }
        finally { normalizingSelection = false; }
    }

    private Rectangle CellRangeDisplayRectangle(CellRange range)
    {
        Rectangle first = grid.GetCellDisplayRectangle(range.Left, range.Top, false), last = grid.GetCellDisplayRectangle(range.Right, range.Bottom, false);
        return first.IsEmpty || last.IsEmpty ? Rectangle.Empty : Rectangle.FromLTRB(first.Left, first.Top, last.Right, last.Bottom);
    }

    private void ApplyFill(CellRange source, CellRange target)
    {
        PushUndo(); loading = true;
        // Fill ranges come from the selection in display coordinates; convert to model rows once so
        // series math and writes stay in model space (identical behavior when nothing is filtered).
        source = new CellRange(source.Left, ModelRow(source.Top), source.Right, ModelRow(source.Bottom));
        target = new CellRange(target.Left, ModelRow(target.Top), target.Right, ModelRow(target.Bottom));
        using (model.BeginUpdate())
        {
            if (target.Bottom > source.Bottom)
            {
                for (int c = source.Left; c <= source.Right; c++)
                {
                    var pattern = Enumerable.Range(source.Top, source.Height).Select(r => model.Rows[r][c].Clone()).ToList();
                    for (int r = source.Bottom + 1; r <= target.Bottom; r++) { int offset = r - source.Bottom - 1; SetFilledCell(r, c, pattern, offset, source.Top + offset % source.Height, c); }
                }
            }
            else if (target.Top < source.Top)
            {
                for (int c = source.Left; c <= source.Right; c++)
                {
                    var pattern = Enumerable.Range(source.Top, source.Height).Select(r => model.Rows[r][c].Clone()).ToList();
                    for (int r = source.Top - 1; r >= target.Top; r--) { int offset = r - source.Top; int patternIndex = Mod(offset, source.Height); SetFilledCell(r, c, pattern, offset, source.Top + patternIndex, c); }
                }
            }
            else if (target.Right > source.Right)
            {
                for (int r = source.Top; r <= source.Bottom; r++)
                {
                    var pattern = Enumerable.Range(source.Left, source.Width).Select(c => model.Rows[r][c].Clone()).ToList();
                    for (int c = source.Right + 1; c <= target.Right; c++) { int offset = c - source.Right - 1; SetFilledCell(r, c, pattern, offset, r, source.Left + offset % source.Width); }
                }
            }
            else if (target.Left < source.Left)
            {
                for (int r = source.Top; r <= source.Bottom; r++)
                {
                    var pattern = Enumerable.Range(source.Left, source.Width).Select(c => model.Rows[r][c].Clone()).ToList();
                    for (int c = source.Left - 1; c >= target.Left; c--) { int offset = c - source.Left; int patternIndex = Mod(offset, source.Width); SetFilledCell(r, c, pattern, offset, r, source.Left + patternIndex); }
                }
            }
        }
        loading = false; grid.ClearSelection();
        for (int r = target.Top; r <= target.Bottom; r++) for (int c = target.Left; c <= target.Right; c++) grid[c, r].Selected = true;
        ReapplyDockedFilterIfActive(); SetDirty();
    }

    private void SetFilledCell(int row, int column, List<CellModel> pattern, int offset, int sourceRow, int sourceColumn)
    {
        var cell = CreateFilledCell(pattern, offset, row, column, sourceRow, sourceColumn);
        model.ReplaceCell(row, column, cell);
    }

    private static CellModel CreateFilledCell(List<CellModel> pattern, int offset, int row, int column, int sourceRow, int sourceColumn)
    {
        var cell = pattern[Mod(offset, pattern.Count)].Clone();
        if (cell.Value.TrimStart().StartsWith('=')) cell.Value = AdjustFormulaReferences(cell.Value, row - sourceRow, column - sourceColumn);
        else if (pattern.Count >= 2 && double.TryParse(pattern[^1].Value, out double last) && double.TryParse(pattern[^2].Value, out double previous) && double.TryParse(pattern[0].Value, out double first)) cell.Value = (offset >= 0 ? last + (last - previous) * (offset + 1) : first + (last - previous) * offset).ToString(System.Globalization.CultureInfo.InvariantCulture);
        else if (pattern.Count >= 2 && DateTime.TryParse(pattern[^1].Value, out DateTime lastDate) && DateTime.TryParse(pattern[^2].Value, out DateTime previousDate) && DateTime.TryParse(pattern[0].Value, out DateTime firstDate)) cell.Value = FormatFilledDate(offset >= 0 ? lastDate.AddTicks((lastDate - previousDate).Ticks * (offset + 1)) : firstDate.AddTicks((lastDate - previousDate).Ticks * offset));
        return cell;
    }

    private static string FormatFilledDate(DateTime value)
    {
        if (value.TimeOfDay == TimeSpan.Zero) return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
    }

    private static int Mod(int value, int divisor) => (value % divisor + divisor) % divisor;

    private static string AdjustFormulaReferences(string formula, int rowOffset, int columnOffset) =>
        FormulaReferenceUpdater.OffsetReferences(formula, rowOffset, columnOffset);

    private void NewDocument()
    {
        ResolveSortPreviewBeforePrimaryDocumentChange(); if (!ConfirmLoseChanges()) return; path = null; filter = null; dirty = false; undo = new(); redo = new(); workbook = WorkbookModel.CreateBlank(); model = workbook.ActiveSheet.Sheet; ReplaceActivePrimaryDocument(); RebindSharedSecondary(); ShowEditor(); RefreshPrimarySheetTabs(); Render(); UpdateTitle();
    }
    private void OpenDialog()
    {
        if (!splitView.Panel2Collapsed) { OpenFileInSplitPane(primary: !secondaryPaneActive); return; }
        ResolveSortPreviewBeforePrimaryDocumentChange(); if (!ConfirmLoseChanges()) return; using var d = new OpenFileDialog { Filter = SpreadsheetOpenFilter };
        if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName);
    }
    private void OpenFile(string file)
    {
        try { ResolveSortPreviewBeforePrimaryDocumentChange(); UseWaitCursor = true; workbook = Path.GetExtension(file).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? XlsxCodec.LoadWorkbook(file) : WorkbookModel.FromSheet(CsvCodec.Load(file), "Sheet1"); model = workbook.ActiveSheet.Sheet; path = file; filter = null; dirty = false; undo = new(); redo = new(); ReplaceActivePrimaryDocument(); RebindSharedSecondary(); sessionRecent.RemoveAll(x => string.Equals(x, file, StringComparison.OrdinalIgnoreCase)); sessionRecent.Insert(0, file); ShowEditor(); RefreshPrimarySheetTabs(); Render(); UpdateTitle(); }
        catch (Exception ex) { ShowNotice("Open failed", "Could not open the file. " + ex.Message); }
        finally { UseWaitCursor = false; }
    }
    private void Save() { if (secondaryPaneActive && !secondarySharesPrimary && !splitView.Panel2Collapsed) SaveSecondary(); else SavePrimary(); }
    private void SavePrimary() { if (path is null) SavePrimaryAs(); else TrySaveTo(path); }
    private bool TrySaveTo(string target)
    {
        try { FlushPendingEdits(grid); if (sortBaselineWorkbook is not null) SaveSortPreview(); workbook.ActiveSheet.Sheet = model; UseWaitCursor = true; if (Path.GetExtension(target).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) XlsxCodec.SaveWorkbook(target, workbook); else { CsvCodec.Save(target, model); if (workbook.Sheets.Count > 1) ShowNotice("CSV saves one sheet", $"Saved the active sheet '{workbook.ActiveSheet.Name}'. Use XLSX to keep all {workbook.Sheets.Count} sheets."); } path = target; dirty = false; UpdateTitle(); UpdateStatus(); return true; }
        catch (Exception ex) { ShowNotice("Save failed", "Could not save the file. " + ex.Message); return false; }
        finally { UseWaitCursor = false; }
    }
    private void SaveAs() { if (secondaryPaneActive && !secondarySharesPrimary && !splitView.Panel2Collapsed) SaveSecondaryAs(); else SavePrimaryAs(); }
    private bool SavePrimaryAs()
    {
        using var d = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV (UTF-8) (*.csv)|*.csv", DefaultExt = "xlsx", AddExtension = true, FileName = path is null ? "Untitled.xlsx" : Path.GetFileName(path) };
        if (d.ShowDialog(this) != DialogResult.OK) return false; return TrySaveTo(d.FileName);
    }

    private void Render()
    {
        loading = true;
        primaryPane.RenderSheet(model);
        ApplyFreeze(); loading = false; if (grid.RowCount > 0 && grid.ColumnCount > 0) grid.CurrentCell = grid[0, 0]; ReapplyDockedFilterIfActive(); RefreshSharedSecondaryFromModel(); UpdateStatus();
    }
    /// <summary>
    /// Opens an undo checkpoint. The mutations since the previous checkpoint are captured now
    /// (cell edits as compact change-sets, structural edits as sheet states); the upcoming
    /// action's own inverse is captured by its next checkpoint or by its structural marker.
    /// </summary>
    private void PushUndo()
    {
        if (loading) return;
        FlushPendingEdits(grid);
        if (sortBaselineWorkbook is not null && sortPreviewApplied) SaveSortPreview();
        ClosePendingUndoStep(undo, workbook.ActiveSheet, model);
        redo.Clear();
    }

    private void ClosePendingUndoStep(Stack<IUndoStep> stack, WorksheetModel worksheet, SheetModel sheet)
    {
        var step = sheet.TakeUndoSegment(worksheet.Id);
        if (step is not null) stack.Push(step);
        if (stack.Count > 80)
        {
            var keep = stack.Take(40).Reverse().ToArray(); stack.Clear(); foreach (var x in keep) stack.Push(x);
        }
    }

    /// <summary>Full-workbook undo entry for commands that change workbook shape (sheet tabs, renames, reorders).</summary>
    private void PushWorkbookStructureUndo()
    {
        workbook.ActiveSheet.Sheet = model;
        ClosePendingUndoStep(undo, workbook.ActiveSheet, model);
        undo.Push(new WorkbookSnapshotStep(workbook.Clone()));
    }

    private bool TryActivatePrimarySheet(Guid worksheetId, out SheetModel sheet)
    {
        int index = workbook.Sheets.FindIndex(entry => entry.Id == worksheetId);
        if (index < 0) { sheet = null!; return false; }
        workbook.ActiveSheetIndex = index;
        sheet = workbook.Sheets[index].Sheet;
        return true;
    }

    private void Undo()
    {
        if (sortBaselineWorkbook is not null)
        {
            bool cancelledPreview = sortPreviewApplied; RevertSortPreview();
            if (cancelledPreview) { countLabel.Text = "Sort preview reverted"; return; }
        }
        FlushPendingEdits(grid);
        ClosePendingUndoStep(undo, workbook.ActiveSheet, model);
        if (undo.Count == 0) return;
        var step = undo.Pop();
        workbook.ActiveSheet.Sheet = model;
        if (step is WorkbookSnapshotStep snapshot)
        {
            redo.Push(new WorkbookSnapshotStep(workbook.Clone()));
            workbook = snapshot.Workbook;
        }
        else
        {
            if (!TryActivatePrimarySheet(step.WorksheetId, out SheetModel liveSheet))
            {
                undo.Push(step); countLabel.Text = "Undo unavailable — worksheet no longer exists"; return;
            }
            redo.Push(step);
            step.Undo(liveSheet);
        }
        model = workbook.ActiveSheet.Sheet; RefreshPrimarySheetTabs(); Render(); SetDirty();
    }
    private void Redo()
    {
        if (sortBaselineWorkbook is not null)
        {
            bool cancelledPreview = sortPreviewApplied; RevertSortPreview();
            if (cancelledPreview) { countLabel.Text = "Sort preview reverted"; return; }
        }
        FlushPendingEdits(grid);
        if (redo.Count == 0) return;
        var step = redo.Pop();
        workbook.ActiveSheet.Sheet = model;
        if (step is WorkbookSnapshotStep snapshot)
        {
            undo.Push(new WorkbookSnapshotStep(workbook.Clone()));
            workbook = snapshot.Workbook;
        }
        else
        {
            if (!TryActivatePrimarySheet(step.WorksheetId, out SheetModel liveSheet))
            {
                redo.Push(step); countLabel.Text = "Redo unavailable — worksheet no longer exists"; return;
            }
            undo.Push(step);
            step.Redo(liveSheet);
        }
        model = workbook.ActiveSheet.Sheet; RefreshPrimarySheetTabs(); Render(); SetDirty();
    }
    private void Copy() { if (grid.GetCellCount(DataGridViewElementStates.Selected) > 0 && grid.GetClipboardContent() is DataObject data) Clipboard.SetDataObject(data); }
    private void Cut() { PushUndo(); Copy(); DeleteContents(false); }
    private void Paste()
    {
        if (grid.CurrentCell is null || !Clipboard.ContainsText()) return; PushUndo(); string[][] rows = Clipboard.GetText().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(x => x.Split('\t')).ToArray();
        int sr = ModelRow(grid.CurrentCell.RowIndex), sc = grid.CurrentCell.ColumnIndex; EnsureGrid(sr + rows.Length, sc + rows.Max(x => x.Length));
        loading = true;
        using (model.BeginUpdate())
            for (int r = 0; r < rows.Length; r++) for (int c = 0; c < rows[r].Length; c++) model.SetCellValue(sr + r, sc + c, rows[r][c]);
        loading = false; ReapplyDockedFilterIfActive(); SetDirty();
    }
    private void DeleteContents() => DeleteContents(true);
    private void DeleteContents(bool snapshot) { if (snapshot) PushUndo(); using (model.BeginUpdate()) foreach (DataGridViewCell cell in grid.SelectedCells) model.SetCellValue(ModelRow(cell.RowIndex), cell.ColumnIndex, ""); ReapplyDockedFilterIfActive(); SetDirty(); }

    private void InsertRow() { int index = ModelRow(grid.CurrentCell?.RowIndex ?? 0); PushUndo(); model.InsertRows(index); RenderSelect(grid.CurrentCell?.RowIndex ?? 0, grid.CurrentCell?.ColumnIndex ?? 0); }
    private void InsertRowBelow() { int lastDisplay = grid.SelectedCells.Count > 0 ? grid.SelectedCells.Cast<DataGridViewCell>().Max(c => c.RowIndex) : grid.CurrentCell?.RowIndex ?? -1; int index = Math.Min(ModelRow(Math.Max(0, Math.Min(lastDisplay, primaryPane.View.DisplayRowCount - 1))) + 1, model.Rows.Count); PushUndo(); model.InsertRows(index); RenderSelect(Math.Min(index, model.Rows.Count - 1), grid.CurrentCell?.ColumnIndex ?? 0); }
    private void DeleteRows() { var indices = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => ModelRow(c.RowIndex)).Distinct().Where(i => i < model.Rows.Count).OrderDescending().ToList(); if (indices.Count == 0) return; PushUndo(); model.DeleteRows(indices); RenderSelect(0, 0); }
    private void MoveRow(int delta) { int i = grid.CurrentCell?.RowIndex ?? -1, j = i + delta; if (i < 0 || j < 0 || j >= primaryPane.View.DisplayRowCount) return; PushUndo(); model.SwapRows(ModelRow(i), ModelRow(j)); RenderSelect(j, grid.CurrentCell?.ColumnIndex ?? 0); }
    private void InsertColumn() { int index = grid.CurrentCell?.ColumnIndex ?? 0; PushUndo(); model.InsertColumns(index); RenderSelect(grid.CurrentCell?.RowIndex ?? 0, index); }
    private void InsertColumnRight() { int index = grid.SelectedCells.Count > 0 ? grid.SelectedCells.Cast<DataGridViewCell>().Max(c => c.ColumnIndex) + 1 : (grid.CurrentCell?.ColumnIndex ?? 0) + 1; index = Math.Min(index, model.ColumnCount); PushUndo(); model.InsertColumns(index); RenderSelect(grid.CurrentCell?.RowIndex ?? 0, Math.Min(index, model.ColumnCount - 1)); }
    private void DeleteColumns() { var indices = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.ColumnIndex).Distinct().Where(i => i < model.ColumnCount).OrderDescending().ToList(); if (indices.Count == 0) return; PushUndo(); model.DeleteColumns(indices); RenderSelect(0, Math.Min(indices.Min(), model.ColumnCount - 1)); }
    private void MoveColumn(int delta) { int i = grid.CurrentCell?.ColumnIndex ?? -1, j = i + delta; if (i < 0 || j < 0 || j >= model.ColumnCount) return; PushUndo(); model.SwapColumns(i, j); RenderSelect(grid.CurrentCell?.RowIndex ?? 0, j); }

    private void Sort(bool ascending)
    {
        int col = grid.CurrentCell?.ColumnIndex ?? -1; if (col < 0 || model.Rows.Count < 2) return;
        PushUndo();
        var body = Enumerable.Range(1, model.Rows.Count - 1).Where(index => model.Rows[index].Any(cell => cell.Value.Length > 0)).ToList();
        var blanks = Enumerable.Range(1, model.Rows.Count - 1).Where(index => model.Rows[index].All(cell => cell.Value.Length == 0)).ToList();
        var keys = body.ToDictionary(index => index, index => EvaluatedCellValue(index, col));
        int direction = ascending ? 1 : -1;
        body.Sort((first, second) =>
        {
            int compared = CompareCells(keys[first], keys[second]) * direction;
            return compared != 0 ? compared : first.CompareTo(second);
        });
        ApplyRowOrder([0, .. body, .. blanks]);
        RenderSelect(1, col); SetDirty();
    }
    private string EvaluatedCellValue(int row, int column, FormulaEngine.FormulaEvaluationContext? context = null)
    {
        if (row < 0 || row >= model.Rows.Count || column < 0 || column >= model.Rows[row].Count) return "";
        return model.EvaluatedValue(row, column, context);
    }
    /// <summary>Text that find/replace operates on for a cell: raw formula source for formulas, evaluated display text otherwise.</summary>
    private string MatchableCellValue(int row, int column, FormulaEngine.FormulaEvaluationContext? context = null)
        => model.IsFormula(row, column) ? model.GetRawValue(row, column) : EvaluatedCellValue(row, column, context);

    /// <summary>Commits an in-progress cell editor into the model before a command snapshots or renders state.
    /// ToolStrip commands and shortcuts never take WinForms focus, so nothing else ends the edit for them.</summary>
    private void FlushPendingEdits(DataGridView pane)
    {
        if (ReferenceEquals(pane, grid)) { if (!loading) primaryPane.FlushPendingEdits(); }
        else if (!secondaryLoading) secondaryPane.FlushPendingEdits();
    }

    // Display↔model row mapping: with an active filter the pane shows a subset of rows in
    // view order, so every command converts grid (display) coordinates to model coordinates.
    private int ModelRow(int displayRow) => primaryPane.ModelRow(displayRow);
    private int SecondaryModelRow(int displayRow) => secondaryPane.ModelRow(displayRow);
    private void ApplyRowOrder(IReadOnlyList<int> oldIndicesInNewOrder) => model.ReorderRows(oldIndicesInNewOrder);
    private static int CompareCells(string a, string b)
    {
        if (decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal x) && decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal y)) return x.CompareTo(y);
        if (DateTime.TryParse(a, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime da) && DateTime.TryParse(b, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime db)) return da.CompareTo(db);
        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
    }
    private void Filter() => ShowFilterBar();
    private void FilterByCurrentValue()
    {
        if (grid.CurrentCell is null) return;
        int column = grid.CurrentCell.ColumnIndex; string value = EvaluatedCellValue(ModelRow(grid.CurrentCell.RowIndex), column);
        ApplyHeaderValueFilter(column, new HashSet<string>(StringComparer.CurrentCultureIgnoreCase) { value });
    }
    private void ClearFilter() { filter = null; headerFilterColumn = -1; headerFilterValues = null; headerFilterOperator = headerFilterConditionValue = null; primaryPane.ResetViewToSheet(); MirrorPrimaryRowVisibilityToSharedSecondary(); grid.Invalidate(); UpdateStatus(); UpdateFindStatus(); }
    private void Freeze() { if (grid.CurrentCell is null) return; model.FrozenRows = grid.CurrentCell.RowIndex; model.FrozenColumns = grid.CurrentCell.ColumnIndex; ApplyFreeze(); SetDirty(); }
    private void Unfreeze() { model.FrozenRows = model.FrozenColumns = 0; ApplyFreeze(); SetDirty(); }
    private void FreezeSelectedRows()
    {
        var rows = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).Distinct().ToList(); if (rows.Count == 0) return;
        model.FrozenRows = rows.Max() + 1; ApplyFreeze(); SetDirty();
    }
    private void FreezeSelectedColumns()
    {
        var columns = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.ColumnIndex).Distinct().ToList(); if (columns.Count == 0) return;
        model.FrozenColumns = columns.Max() + 1; ApplyFreeze(); SetDirty();
    }
    private void UnfreezeRows() { model.FrozenRows = 0; ApplyFreeze(); SetDirty(); }
    private void UnfreezeColumns() { model.FrozenColumns = 0; ApplyFreeze(); SetDirty(); }
    private void ApplyFreeze() { foreach (DataGridViewColumn c in grid.Columns) c.Frozen = c.Index < model.FrozenColumns; foreach (DataGridViewRow r in grid.Rows) r.Frozen = r.Index < model.FrozenRows; }

    private void Find() => ShowFindBar(false);
    private void Replace() => ShowFindBar(true);
    private static string ReplaceInsensitive(string value, string find, string replacement) { int i = 0; while ((i = value.IndexOf(find, i, StringComparison.CurrentCultureIgnoreCase)) >= 0) { value = value[..i] + replacement + value[(i + find.Length)..]; i += replacement.Length; } return value; }

    private void SetBackground() => PickColor(true);
    private void SetForeground() => PickColor(false);
    private void PickColor(bool background)
    {
        if (CurrentFormattedCell() is not CellModel current) return;
        Color effectiveBackground = current.BackColor ?? Theme.CellBackground;
        Color initial = background ? effectiveBackground : current.ForeColor ?? Theme.AdaptiveCellText(effectiveBackground);
        using var dialog = new CellColorDialog(background, initial);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        CellEdit edit = dialog.ResetToDefault
            ? background ? CellEdit.ResetBackgroundColor() : CellEdit.ResetTextColor()
            : CellEdit.Format(background ? dialog.SelectedColor : null, background ? null : dialog.SelectedColor);
        ApplyColorEdit(edit);
    }
    private void ResetColors() => ApplyColorEdit(CellEdit.ResetColors());
    private void ApplyColorEdit(CellEdit edit) => ApplyFormatting(_ => edit);
    private void ChangeFontSize(float delta) => ApplyFormatting(cell => CellEdit.Format(fontSize: Math.Clamp((cell.FontSize ?? CellModel.DefaultFontSize) + delta, 6F, 72F)));
    private void ToggleBold() { if (CurrentFormattedCell() is not CellModel cell) return; ApplyFormatting(_ => CellEdit.Format(bold: !cell.Bold)); }
    private void ToggleItalic() { if (CurrentFormattedCell() is not CellModel cell) return; ApplyFormatting(_ => CellEdit.Format(italic: !cell.Italic)); }
    private void ToggleUnderline() { if (CurrentFormattedCell() is not CellModel cell) return; ApplyFormatting(_ => CellEdit.Format(underline: !cell.Underline)); }
    private void SetHorizontalAlignment(CellHorizontalAlignment alignment) => ApplyFormatting(_ => CellEdit.Format(horizontalAlignment: alignment));
    private void SetVerticalAlignment(CellVerticalAlignment alignment) => ApplyFormatting(_ => CellEdit.Format(verticalAlignment: alignment));
    private void ClearFormatting() => ApplyFormatting(_ => CellEdit.ResetFormatting());

    private CellModel? CurrentFormattedCell()
    {
        bool secondary = secondaryPaneActive && !splitView.Panel2Collapsed;
        DataGridView activeGrid = secondary ? secondaryGrid : grid;
        SheetModel? activeModel = secondary ? secondaryModel : model;
        WorksheetPaneController pane = secondary ? secondaryPane : primaryPane;
        if (activeModel is null || activeGrid.CurrentCell is not { } current || current.RowIndex >= pane.View.DisplayRowCount) return null;
        return activeModel.GetCell(pane.ModelRow(current.RowIndex), current.ColumnIndex);
    }

    private void ApplyFormatting(Func<CellModel, CellEdit> editForCell)
    {
        bool secondary = secondaryPaneActive && !splitView.Panel2Collapsed;
        DataGridView activeGrid = secondary ? secondaryGrid : grid;
        SheetModel? activeModel = secondary ? secondaryModel : model;
        WorksheetPaneController pane = secondary ? secondaryPane : primaryPane;
        List<DataGridViewCell> selected = activeGrid.SelectedCells.Cast<DataGridViewCell>().ToList();
        if (activeModel is null || selected.Count == 0) return;
        List<int> affectedModelRows = selected.Select(cell => pane.ModelRow(cell.RowIndex)).Distinct().ToList();
        if (secondary) PushSecondaryEditUndo(); else PushUndo();
        int version = activeModel.Version;
        bool rowHeightMayChange = false;
        using (activeModel.BeginUpdate())
            foreach (DataGridViewCell selectedCell in selected)
            {
                int row = pane.ModelRow(selectedCell.RowIndex);
                CellEdit edit = editForCell(activeModel.GetCell(row, selectedCell.ColumnIndex));
                rowHeightMayChange |= edit.FontSize is not null || edit.ClearFormatting;
                activeModel.SetCell(new CellAddress(row, selectedCell.ColumnIndex), edit);
            }
        if (rowHeightMayChange)
        {
            pane.RefreshRowHeights(selected.Select(cell => cell.RowIndex));
            if (secondarySharesPrimary && !splitView.Panel2Collapsed)
            {
                WorksheetPaneController otherPane = secondary ? primaryPane : secondaryPane;
                otherPane.RefreshRowHeights(affectedModelRows.Select(otherPane.DisplayRow));
            }
        }
        if (activeModel.Version != version)
        {
            if (secondary) MarkSecondaryEdited(); else SetDirty();
        }
        UpdateToolbarFormattingState();
    }

    private void UpdateToolbarFormattingState()
    {
        if (boldButton is null) return;
        CellModel? cell = CurrentFormattedCell();
        bool available = cell is not null;
        foreach (ToolStripButton button in new[] { boldButton, italicButton, underlineButton, alignLeftButton, alignCenterButton, alignRightButton, alignTopButton, alignMiddleButton, alignBottomButton }) button.Enabled = available;
        boldButton.Checked = cell?.Bold == true; italicButton.Checked = cell?.Italic == true; underlineButton.Checked = cell?.Underline == true;
        CellHorizontalAlignment horizontal = cell?.HorizontalAlignment ?? CellHorizontalAlignment.Left;
        CellVerticalAlignment vertical = cell?.VerticalAlignment ?? CellVerticalAlignment.Middle;
        alignLeftButton.Checked = available && horizontal == CellHorizontalAlignment.Left;
        alignCenterButton.Checked = available && horizontal == CellHorizontalAlignment.Center;
        alignRightButton.Checked = available && horizontal == CellHorizontalAlignment.Right;
        alignTopButton.Checked = available && vertical == CellVerticalAlignment.Top;
        alignMiddleButton.Checked = available && vertical == CellVerticalAlignment.Middle;
        alignBottomButton.Checked = available && vertical == CellVerticalAlignment.Bottom;
    }
    private void AutoSizeColumns() { foreach (int c in grid.SelectedCells.Cast<DataGridViewCell>().Select(x => x.ColumnIndex).Distinct()) grid.Columns[c].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells; BeginInvoke(() => { foreach (DataGridViewColumn c in grid.Columns) if (c.AutoSizeMode != DataGridViewAutoSizeColumnMode.None) { int w = Math.Min(c.Width, 400); c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; c.Width = w; } }); }

    private void EnsureGrid(int rows, int columns) { if (rows <= grid.RowCount && columns <= grid.ColumnCount) return; FlushPendingEdits(grid); model.EnsureSize(Math.Max(rows, grid.RowCount), Math.Max(columns, grid.ColumnCount)); Render(); }
    private void RenderSelect(int row, int col) { Render(); grid.CurrentCell = grid[Math.Max(0, col), Math.Max(0, row)]; SetDirty(); }
    private static string ColumnName(int index) => CellAddress.ColumnName(index);
    private void SetDirty()
    {
        dirty = true;
        // The sort panel is non-modal. Edits made before a preview must become
        // part of its baseline so applying or reverting a sort cannot erase them.
        if (sortBaselineWorkbook is not null && !sortPreviewApplied)
        {
            FlushPendingEdits(grid);
            workbook.ActiveSheet.Sheet = model;
            sortBaselineWorkbook = workbook.Clone();
            sortBaselineDirty = true;
        }
        RefreshSharedSecondaryFromModel(); UpdateTitle(); UpdateStatus(); if (findBar.Visible) UpdateFindStatus();
    }
    private void SetDirtyCell(int row, int column)
    {
        dirty = true;
        if (sortBaselineWorkbook is not null && !sortPreviewApplied)
        {
            FlushPendingEdits(grid); workbook.ActiveSheet.Sheet = model; sortBaselineWorkbook = workbook.Clone(); sortBaselineDirty = true;
        }
        RefreshSharedSecondaryCell(row, column); UpdateTitle(); UpdateStatus(); if (findBar.Visible) UpdateFindStatus();
    }
    private void UpdateTitle() { string name = path is null ? "Untitled" : Path.GetFileName(path); Text = ""; primaryPaneTitle.Text = name; CapturePrimaryDocument(); if (secondarySharesPrimary) secondaryPath = path; RefreshDocumentTabs(); UpdateFileTabChrome(); }
    private void UpdateStatus()
    {
        bool secondary = secondaryPaneActive && !splitView.Panel2Collapsed; DataGridView activeGrid = secondary ? secondaryGrid : grid;
        int visible = activeGrid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible); countLabel.Text = $"{visible:N0} rows × {activeGrid.ColumnCount:N0} columns";
        dirtyLabel.Text = secondary ? (secondarySharesPrimary ? (dirty ? "  Modified " : "  Saved ") : (secondaryDirty ? "  Modified " : "  Saved ")) : dirty ? "  Modified " : "  Saved ";
        if (activeGrid.CurrentCell is null) { positionLabel.Text = ""; typeLabel.Text = ""; return; }
        SheetModel? activeModel = secondary ? secondaryModel : model;
        var activePane = secondary ? secondaryPane : primaryPane;
        var selected = activeGrid.SelectedCells.Cast<DataGridViewCell>().ToList();
        if (selected.Count > 1)
        {
            int top = selected.Min(c => c.RowIndex), bottom = selected.Max(c => c.RowIndex), left = selected.Min(c => c.ColumnIndex), right = selected.Max(c => c.ColumnIndex);
            int characters = selected.Sum(c => (activeModel?.EvaluatedValue(activePane.ModelRow(c.RowIndex), c.ColumnIndex) ?? "").Length); positionLabel.Text = $" {ColumnName(left)}{activePane.ModelRow(top) + 1}:{ColumnName(right)}{activePane.ModelRow(bottom) + 1}"; typeLabel.Text = $"{selected.Count:N0} cells · {characters:N0} chars";
        }
        else
        {
            int row = activePane.ModelRow(activeGrid.CurrentCell.RowIndex), column = activeGrid.CurrentCell.ColumnIndex;
            positionLabel.Text = $" {ColumnName(column)}{row + 1}";
            if (activeModel is not null && activeModel.IsFormula(row, column))
            {
                FormulaResult result = FormulaEngine.Evaluate(activeModel, row, column);
                typeLabel.Text = result.Success ? "Formula" : result.Error ?? "Formula error";
            }
            else
            {
                string value = activeModel?.GetRawValue(row, column) ?? "";
                typeLabel.Text = double.TryParse(value, out _) ? "Number" : DateTime.TryParse(value, out _) ? "Date" : bool.TryParse(value, out _) ? "Boolean" : string.IsNullOrEmpty(value) ? "Empty" : $"Text · {value.Length:N0} chars";
            }
        }
    }
    private bool ConfirmLoseChanges()
    {
        if (!dirty) return true;
        string name = path is null ? "Untitled" : Path.GetFileName(path); DialogResult result = MessageBox.Show(this, $"Save changes to {name}?", "SheetLite", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return result == DialogResult.No || result == DialogResult.Yes && (path is null ? SavePrimaryAs() : TrySaveTo(path));
    }
    private void OnClosing(object? sender, FormClosingEventArgs e) { if (!ConfirmAllDocumentChanges()) e.Cancel = true; }
    internal bool TryProcessAppShortcut(Keys keyData)
    {
        bool textFocused = findBox.Focused || replaceBox.Focused || filterValue.Focused || filterValue2.Focused || sqlEditor.Focused || helpSearch.Focused || grid.EditingControl?.Focused == true || secondaryGrid.EditingControl?.Focused == true;
        bool secondary = secondaryPaneActive && !splitView.Panel2Collapsed;
        bool gridCommandsBlocked = helpPage.Visible || infoPanel.Visible || welcome.Visible;
        Action? action = keyData switch
        {
            Keys.Control | Keys.N => NewDocument,
            Keys.Control | Keys.O => OpenDialog,
            Keys.Control | Keys.Shift | Keys.O => OpenAnotherDialog,
            Keys.Control | Keys.S when !welcome.Visible => Save,
            Keys.Control | Keys.Shift | Keys.S when !welcome.Visible => SaveAs,
            Keys.Control | Keys.Z when !textFocused && !gridCommandsBlocked => secondary ? SecondaryUndo : Undo,
            Keys.Control | Keys.Y when !textFocused && !gridCommandsBlocked => secondary ? SecondaryRedo : Redo,
            Keys.Control | Keys.X when !textFocused && !gridCommandsBlocked => secondary ? SecondaryCut : Cut,
            Keys.Control | Keys.C when !textFocused && !gridCommandsBlocked => secondary ? SecondaryCopy : Copy,
            Keys.Control | Keys.V when !textFocused && !gridCommandsBlocked => secondary ? SecondaryPaste : Paste,
            Keys.Delete when !textFocused && !gridCommandsBlocked => secondary ? SecondaryDeleteContents : DeleteContents,
            Keys.Control | Keys.F when helpPage.Visible => () => { helpSearch.Focus(); helpSearch.SelectAll(); },
            Keys.Control | Keys.F when !gridCommandsBlocked => Find,
            Keys.Control | Keys.H when !gridCommandsBlocked => Replace,
            Keys.Control | Keys.L when !gridCommandsBlocked => Filter,
            Keys.Control | Keys.Shift | Keys.L when !gridCommandsBlocked => ClearFilter,
            Keys.Control | Keys.Shift | Keys.T when !gridCommandsBlocked => ShowSortPanel,
            Keys.Control | Keys.Shift | Keys.Up when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Insert row above", InsertRow),
            Keys.Control | Keys.Shift | Keys.Down when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Insert row below", InsertRowBelow),
            Keys.Control | Keys.Shift | Keys.Left when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Insert column left", InsertColumn),
            Keys.Control | Keys.Shift | Keys.Right when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Insert column right", InsertColumnRight),
            Keys.Control | Keys.Subtract when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Delete rows", DeleteRows),
            Keys.Control | Keys.Shift | Keys.Subtract when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Delete columns", DeleteColumns),
            Keys.Alt | Keys.Up when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Move row up", () => MoveRow(-1)),
            Keys.Alt | Keys.Down when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Move row down", () => MoveRow(1)),
            Keys.Alt | Keys.Left when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Move column left", () => MoveColumn(-1)),
            Keys.Alt | Keys.Right when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Move column right", () => MoveColumn(1)),
            // Keep the original one-sided shortcuts as undisplayed compatibility aliases.
            Keys.Control | Keys.Shift | Keys.R when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Insert row above", InsertRow),
            Keys.Control | Keys.Shift | Keys.C when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Insert column left", InsertColumn),
            Keys.Control | Keys.Up when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Sort ascending", () => Sort(true)),
            Keys.Control | Keys.Down when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Sort descending", () => Sort(false)),
            Keys.Control | Keys.Oem3 when !gridCommandsBlocked => () => ToggleSqlConsole(),
            Keys.Control | Keys.Alt | Keys.S when !gridCommandsBlocked => ToggleSplitView,
            Keys.Control | Keys.Shift | Keys.F when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Freeze panes", Freeze),
            Keys.Control | Keys.B when !textFocused && !gridCommandsBlocked => ToggleBold,
            Keys.Control | Keys.I when !textFocused && !gridCommandsBlocked => ToggleItalic,
            Keys.Control | Keys.U when !textFocused && !gridCommandsBlocked => ToggleUnderline,
            Keys.Control | Keys.Shift | Keys.Space when !textFocused && !gridCommandsBlocked => ClearFormatting,
            Keys.Control | Keys.Alt | Keys.A when !textFocused && !gridCommandsBlocked => () => RunPrimaryCommand("Auto-size columns", AutoSizeColumns),
            Keys.Escape when helpPage.Visible => CloseHelpPage,
            Keys.Escape when infoPanel.Visible => () => { infoPanel.Visible = false; RefreshCommandHost(); },
            Keys.F1 => () => ShowHelpPage(),
            _ => null
        };
        if (action is null) return false;
        action(); return true;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData) =>
        TryProcessAppShortcut(keyData) || base.ProcessCmdKey(ref message, keyData);
    private void About() => ShowNotice("About SheetLite", $"SheetLite {AppVersion} — a fast, portable CSV/XLSX workbook editor with multiple worksheets, formulas, reversible sorting, split view, and local SQL. No telemetry. No installer.");
    private void ShowNotice(string title, string text) { if (welcome.Visible) ShowWelcomeInfo(title, text); else ShowInfoPanel(title, text); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Application.RemoveMessageFilter(resizeCursorFilter); foreach (Image glyph in chromeGlyphs) glyph.Dispose(); boldCellFont.Dispose(); recentToolTip.Dispose(); sqlToolTips.Dispose(); findDebounce.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class SmoothDataGridView : DataGridView
{
    public SmoothDataGridView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        UpdateStyles();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        FindForm() is MainForm form && form.TryProcessAppShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
}

internal sealed class WindowBorderOverlay : Control
{
    public WindowBorderOverlay()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface; TabStop = false;
    }

    public void RefreshShape()
    {
        if (Width < 8 || Height < 8) return;
        bool rounded = FindForm()?.WindowState != FormWindowState.Maximized;
        using var outer = BorderPath(new RectangleF(0, 0, Width, Height), rounded ? ScaledDiameter : 0);
        using var inner = BorderPath(new RectangleF(3, 3, Width - 6, Height - 6), rounded ? Math.Max(2, ScaledDiameter - 6) : 0);
        var next = new Region(outer); next.Exclude(inner); Region? old = Region; Region = next; old?.Dispose(); Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RefreshShape(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); bool rounded = FindForm()?.WindowState != FormWindowState.Maximized;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality; e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        using var outer = BorderPath(new RectangleF(0, 0, Width, Height), rounded ? ScaledDiameter : 0);
        using var inner = BorderPath(new RectangleF(1, 1, Width - 2, Height - 2), rounded ? Math.Max(2, ScaledDiameter - 2) : 0);
        using var borderBrush = new SolidBrush(Theme.CurrentLine); using var surfaceBrush = new SolidBrush(Theme.Surface);
        e.Graphics.FillPath(borderBrush, outer); e.Graphics.FillPath(surfaceBrush, inner);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x84) { message.Result = (IntPtr)(-1); return; }
        base.WndProc(ref message);
    }

    private float ScaledDiameter => Math.Max(12F, 12F * DeviceDpi / 96F);

    private static GraphicsPath BorderPath(RectangleF bounds, float diameter)
    {
        var path = new GraphicsPath();
        if (diameter <= 0)
        {
            path.AddRectangle(bounds); return path;
        }
        diameter = Math.Min(diameter, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90); path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90); path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class WindowChromeButton : Button
{
    private bool hovered, pressed;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; init; } = Theme.Hover;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color PressedColor { get; init; } = Theme.Selection;

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (hovered || pressed)
        {
            using var brush = new SolidBrush(pressed ? PressedColor : HoverColor);
            float scale = DeviceDpi / 96F, horizontalInset = Math.Max(5F, 6F * scale), verticalInset = Math.Max(4F, 5F * scale);
            var bounds = new RectangleF(horizontalInset, verticalInset, Math.Max(1F, Width - horizontalInset * 2F), Math.Max(1F, Height - verticalInset * 2F));
            float diameter = Math.Min(Math.Max(7F, 8F * scale), Math.Min(bounds.Width, bounds.Height));
            using var path = RoundedRectangle(bounds, diameter);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality; e.Graphics.FillPath(brush, path);
        }
        if (Image is not null)
        {
            int x = (Width - Image.Width) / 2, y = (Height - Image.Height) / 2; e.Graphics.DrawImageUnscaled(Image, x, y);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90); path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90); path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
        return path;
    }
}

internal sealed class ResizeCursorMessageFilter(MainForm form) : IMessageFilter
{
    private bool resizeCursorSet;

    public bool PreFilterMessage(ref Message message)
    {
        if (form.IsDisposed || !form.Visible || message.Msg is not (0x20 or 0x200 or 0xA0)) return false;
        Cursor? cursor = form.ResizeCursorAtScreen(Cursor.Position);
        if (cursor is not null)
        {
            Cursor.Current = cursor; resizeCursorSet = true; return message.Msg == 0x20;
        }
        if (resizeCursorSet) { Cursor.Current = Cursors.Default; resizeCursorSet = false; }
        return false;
    }
}

internal sealed class DividerStatusStrip : StatusStrip
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var divider = new Pen(Theme.CurrentLine);
        e.Graphics.DrawLine(divider, 0, 0, ClientSize.Width, 0);
    }
}
