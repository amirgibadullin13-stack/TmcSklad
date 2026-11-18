using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace Tmc;

public record F(string Label, string Value, string[]? Options = null);
public class Row { public object? Obj { get; set; } public string[] V { get; set; } = Array.Empty<string>(); }

public static class Dlg
{
    static Window Win(string t)
    {
        var w = new Window { Title = t, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 320, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var o = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive);
        if (o != null && o != w) w.Owner = o; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w;
    }

    static Button Ok(Window w, Action<bool> set)
    {
        var b = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 3, 12, 3), HorizontalAlignment = HorizontalAlignment.Right };
        b.Click += (_, _) => { set(true); w.Close(); };
        return b;
    }

    public static string[]? Ask(string title, params F[] fields)
    {
        var w = Win(title);
        var sp = new StackPanel { Margin = new Thickness(12) };
        var ctrls = new List<Control>();
        foreach (var f in fields)
        {
            sp.Children.Add(new TextBlock { Text = f.Label, Margin = new Thickness(0, 6, 0, 2) });
            Control c = f.Options == null
                ? new TextBox { Text = f.Value }
                : new ComboBox { ItemsSource = f.Options, SelectedItem = f.Value };
            if (c is ComboBox cb && cb.SelectedIndex < 0 && f.Options!.Length > 0) cb.SelectedIndex = 0;
            ctrls.Add(c); sp.Children.Add(c);
        }
        bool ok = false;
        sp.Children.Add(Ok(w, v => ok = v));
        w.Content = sp;
        if (ctrls.FirstOrDefault() is TextBox t0) w.Loaded += (_, _) => t0.Focus();
        w.ShowDialog();
        return ok ? ctrls.Select(c => c is TextBox t ? t.Text : ((ComboBox)c).SelectedItem as string ?? "").ToArray() : null;
    }

    public static bool[]? Pick(string title, string[] opts, bool[] sel)
    {
        var w = Win(title);
        var sp = new StackPanel { Margin = new Thickness(12) };
        sp.Children.Add(new TextBlock { Text = "Категории, доступные для выдачи:" });
        var cbs = opts.Select((o, i) => new CheckBox { Content = o, IsChecked = sel[i], Margin = new Thickness(0, 5, 0, 0) }).ToList();
        cbs.ForEach(c => sp.Children.Add(c));
        bool ok = false;
        sp.Children.Add(Ok(w, v => ok = v));
        w.Content = sp;
        w.ShowDialog();
        return ok ? cbs.Select(c => c.IsChecked == true).ToArray() : null;
    }
}

public class Crud : DockPanel
{
    public DataGrid G = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false };
    public StackPanel Bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
    readonly Func<List<Row>> load;

    public Crud(string[] headers, Func<List<Row>> load, Action? add = null, Action<object>? edit = null, Action<object>? del = null, string? hint = null)
    {
        this.load = load;
        Margin = new Thickness(8);
        for (int i = 0; i < headers.Length; i++)
            G.Columns.Add(new DataGridTextColumn { Header = headers[i], Binding = new Binding($"V[{i}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        if (add != null) Btn("Добавить", () => { add(); Refresh(); });
        if (edit != null) Btn("Изменить", () => { if (Sel != null) { edit(Sel); Refresh(); } else H.Err("Выберите строку"); });
        if (del != null) Btn("Удалить", () => { if (Sel != null) { del(Sel); Refresh(); } else H.Err("Выберите строку"); });
        SetDock(Bar, Dock.Top);
        Children.Add(Bar);
        if (hint != null)
        {
            var h = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 6) };
            SetDock(h, Dock.Top);
            Children.Add(h);
        }
        Children.Add(G);
        Refresh();
    }

    public object? Sel => (G.SelectedItem as Row)?.Obj;
    public void Refresh() => G.ItemsSource = load();

    public Button Btn(string text, Action a)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
        b.Click += (_, _) => a();
        Bar.Children.Add(b);
        return b;
    }
}

public class LoginWindow : Window
{
    public LoginWindow()
    {
        Title = "Вход"; Width = 280; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var l = new TextBox(); var p = new PasswordBox();
        var err = new TextBlock { Foreground = Brushes.Red, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "Войти", IsDefault = true, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        ok.Click += (_, _) =>
        {
            var u = Store.D.Users.FirstOrDefault(x => x.Login.Equals(l.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (u != null && Store.Pw(u.Salt, p.Password) == u.PwHash)
            {
                if (u.Password == "") { u.Password = p.Password; Store.Save(); }
                Store.Cur = u; DialogResult = true;
            }
            else err.Text = "Неверный логин или пароль";
        };
        var sp = new StackPanel { Margin = new Thickness(12) };
        sp.Children.Add(new TextBlock { Text = "Логин" }); sp.Children.Add(l);
        sp.Children.Add(new TextBlock { Text = "Пароль", Margin = new Thickness(0, 6, 0, 0) }); sp.Children.Add(p);
        sp.Children.Add(err); sp.Children.Add(ok);
        Content = sp;
        Loaded += (_, _) => l.Focus();
    }
}

public class IssueWindow : Window
{
    public Issue? Result;
    readonly List<IssueLine> lines = new();
    readonly ListBox lb = new() { MinHeight = 120, Margin = new Thickness(0, 8, 0, 0) };

    public IssueWindow()
    {
        Title = "Новая выдача"; Width = 480; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var u = Store.Cur!;
        var items = H.D.Items.Where(i => u.IsAdmin || u.CategoryIds.Contains(i.CategoryId)).OrderBy(H.Label).ToList();
        var rec = new TextBox(); var note = new TextBox(); var qty = new TextBox { Width = 70, Margin = new Thickness(6, 0, 6, 0) };
        var cb = new ComboBox { ItemsSource = items.Select(i => $"{H.Label(i)}  (ост. {H.N(H.Stock(i.Id))} {i.Unit})").ToList(), SelectedIndex = items.Count > 0 ? 0 : -1 };

        var add = new Button { Content = "Добавить", Padding = new Thickness(8, 2, 8, 2) };
        bool TryAdd()
        {
            if (cb.SelectedIndex < 0 || !H.TryQty(qty.Text, out var q)) { H.Err("Выберите товар и введите количество — число больше нуля, например 1"); return false; }
            var it = items[cb.SelectedIndex];
            var ex = lines.FirstOrDefault(l => l.ItemId == it.Id);
            var total = (ex?.Qty ?? 0) + q;
            if (total > H.Stock(it.Id)) { H.Err($"Недостаточно на складе. Остаток: {H.N(H.Stock(it.Id))}"); return false; }
            if (ex == null) lines.Add(new IssueLine { ItemId = it.Id, Qty = q }); else ex.Qty = total;
            qty.Clear(); Fill();
            return true;
        }
        add.Click += (_, _) => TryAdd();
        var rm = new Button { Content = "Убрать строку", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        rm.Click += (_, _) => { if (lb.SelectedIndex >= 0) { lines.RemoveAt(lb.SelectedIndex); Fill(); } };
        var ok = new Button { Content = "Сформировать акт", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(rec.Text)) { H.Err("Укажите получателя"); return; }
            if (qty.Text.Trim() != "" && !TryAdd()) return;   // количество введено, но «Добавить» не нажато — добавим сами
            if (lines.Count == 0) { H.Err("В списке нет товаров. Выберите товар, введите количество и нажмите «Добавить» — товар появится в большом списке ниже."); return; }
            foreach (var l in lines) if (l.Qty > H.Stock(l.ItemId)) { H.Err("Остатки изменились, проверьте количество"); return; }
            Result = new Issue { Number = H.D.NextAct++, Date = DateTime.Now, Recipient = rec.Text.Trim(), Note = note.Text, User = u.FullName, Lines = lines.ToList() };
            DialogResult = true;
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Кол-во:", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(qty); row.Children.Add(add); row.Children.Add(rm);

        var sp = new StackPanel { Margin = new Thickness(12) };
        sp.Children.Add(new TextBlock { Text = "1) Укажите получателя  2) выберите товар и количество, нажмите «Добавить» (товар появится в списке ниже)  3) нажмите «Сформировать акт».", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        sp.Children.Add(new TextBlock { Text = "Получатель (ФИО / подразделение)" }); sp.Children.Add(rec);
        sp.Children.Add(new TextBlock { Text = "Примечание", Margin = new Thickness(0, 6, 0, 0) }); sp.Children.Add(note);
        sp.Children.Add(new TextBlock { Text = "Товар", Margin = new Thickness(0, 6, 0, 2) }); sp.Children.Add(cb);
        sp.Children.Add(row); sp.Children.Add(lb); sp.Children.Add(ok);
        Content = sp;
    }

    void Fill() => lb.ItemsSource = lines.Select(l => { var it = H.Find(l.ItemId); return (it != null ? H.Label(it) : "?") + " — " + H.N(l.Qty); }).ToList();
}
