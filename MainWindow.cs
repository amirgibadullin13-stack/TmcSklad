using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Tmc;

public class MainWindow : Window
{
    static AppData D => Store.D;
    static bool Adm => Store.Cur!.IsAdmin;
    readonly TabControl tc = new();
    bool busy;

    public MainWindow()
    {
        Title = $"Учёт ТМЦ — {Store.Cur!.FullName}";
        Width = 960; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        tc.SelectionChanged += (_, e) => { if (e.Source == tc) (tc.SelectedContent as Crud)?.Refresh(); };
        Tab("Приход", ReceiptsTab());
        Tab("Выдача", IssuesTab());
        Tab("Остатки", StockTab());
        if (Adm)
        {
            Tab("Категории", CatsTab());
            Tab("Номенклатура", ItemsTab());
            Tab("Пользователи", UsersTab());
            Tab("Копии", BackupTab());
        }
        Content = tc;
    }

    void Tab(string h, UIElement c) => tc.Items.Add(new TabItem { Header = h, Content = c });

    // ---------- Приход ----------
    Crud ReceiptsTab() => new(
        new[] { "Дата", "Категория", "Наименование", "Кол-во", "Ед.", "Примечание", "Принял" },
        () => D.Receipts.OrderByDescending(r => r.Date).Select(r =>
        {
            var it = H.Find(r.ItemId);
            return new Row { Obj = r, V = new[] { r.Date.ToString("dd.MM.yyyy"), it == null ? "?" : H.Cat(it.CategoryId), it?.Name ?? "?", H.N(r.Qty), it?.Unit ?? "", r.Note, r.User } };
        }).ToList(),
        () => ReceiptDlg(null),
        Adm ? o => ReceiptDlg((Receipt)o) : null,
        Adm ? o => { if (H.Yes("Удалить запись прихода?")) { D.Receipts.Remove((Receipt)o); Store.Save(); } } : null,
        "Здесь фиксируется поступление товара на склад: нажмите «Добавить», выберите товар и введите количество — остаток вырастет. Нужного товара нет в списке? Его создаёт администратор: вкладки «Категории» → «Номенклатура».");

    void ReceiptDlg(Receipt? r, Item? pre = null)
    {
        if (!D.Items.Any()) { H.Err(Adm ? "В списке пока нет товаров. Сначала создайте категорию и товар: вкладки «Категории» → «Номенклатура»." : "В списке пока нет товаров. Попросите администратора добавить их."); return; }
        var opts = D.Items.Select(H.Label).OrderBy(x => x).ToArray();
        var cur = r != null ? H.Label(H.Find(r.ItemId)!) : pre != null ? H.Label(pre) : opts[0];
        var v = Dlg.Ask(r == null ? "Приход" : "Изменить приход",
            new F("Дата (дд.мм.гггг)", (r?.Date ?? DateTime.Today).ToString("dd.MM.yyyy")),
            new F("Товар", cur, opts),
            new F("Количество (сколько поступило)", r == null ? "" : H.N(r.Qty)),
            new F("Примечание", r?.Note ?? ""));
        if (v == null) return;
        if (!H.TryDate(v[0], out var d)) { H.Err("Неверная дата. Введите в формате дд.мм.гггг, например 17.11.2026"); return; }
        if (!H.TryQty(v[2], out var q)) { H.Err("Неверное количество. Введите число больше нуля, например 5 или 2,5"); return; }
        var item = D.Items.First(i => H.Label(i) == v[1]);
        if (r == null) { r = new Receipt { User = Store.Cur!.FullName }; D.Receipts.Add(r); }
        r.Date = d; r.ItemId = item.Id; r.Qty = q; r.Note = v[3];
        Store.Save();
    }

    // ---------- Выдача ----------
    Crud IssuesTab()
    {
        var c = new Crud(
            new[] { "№", "Дата", "Получатель", "Выдал", "Позиции" },
            () => D.Issues.OrderByDescending(a => a.Number).Select(a => new Row
            {
                Obj = a,
                V = new[] { a.Number.ToString(), a.Date.ToString("dd.MM.yyyy HH:mm"), a.Recipient, a.User,
                    string.Join("; ", a.Lines.Select(l => $"{H.Find(l.ItemId)?.Name} × {H.N(l.Qty)}")) }
            }).ToList(),
            NewIssue, null,
            Adm ? o => { if (H.Yes("Удалить выдачу? Товар вернётся на остаток.")) { D.Issues.Remove((Issue)o); Store.Save(); } } : null,
            "Нажмите «Добавить»: укажите получателя, выберите товар и количество, нажмите «Добавить» в окне и затем «Сформировать акт». Остаток уменьшится автоматически. Выдать можно только то, что есть на остатке (сначала оформите «Приход»).");
        c.Btn("Открыть акт", () => { if (c.Sel is Issue a) H.OpenAct(a); else H.Err("Выберите строку"); });
        return c;
    }

    void NewIssue()
    {
        var u = Store.Cur!;
        if (!D.Items.Any(i => u.IsAdmin || u.CategoryIds.Contains(i.CategoryId))) { H.Err(Adm ? "Нет товаров для выдачи. Создайте товар (вкладка «Номенклатура») и оформите его приход." : "Вам не открыт доступ ни к одной категории. Обратитесь к администратору."); return; }
        var w = new IssueWindow { Owner = this };
        if (w.ShowDialog() == true)
        {
            D.Issues.Add(w.Result!);
            Store.Save();
            H.OpenAct(w.Result!);
        }
    }

    // ---------- Остатки ----------
    Crud StockTab()
    {
        var filter = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 6, 0) };
        Crud? st = null;
        filter.SelectionChanged += (_, _) => { if (!busy) st?.Refresh(); };
        st = new Crud(
            new[] { "Категория", "Наименование", "Ед.", "Приход", "Расход", "Остаток" },
            () =>
            {
                busy = true;
                var sel = filter.SelectedItem as string ?? "Все категории";
                var names = new List<string> { "Все категории" };
                names.AddRange(D.Categories.Select(c => c.Name).OrderBy(x => x));
                filter.ItemsSource = names;
                filter.SelectedItem = names.Contains(sel) ? sel : names[0];
                busy = false;
                var f = filter.SelectedItem as string;
                return D.Items.Where(i => f == "Все категории" || H.Cat(i.CategoryId) == f)
                    .OrderBy(i => H.Cat(i.CategoryId)).ThenBy(i => i.Name)
                    .Select(i => new Row { Obj = i, V = new[] { H.Cat(i.CategoryId), i.Name, i.Unit, H.N(H.Rcv(i.Id)), H.N(H.Iss(i.Id)), H.N(H.Stock(i.Id)) } }).ToList();
            },
            hint: "Остаток = Приход − Расход. Если везде нули — товар ещё не оприходован: откройте вкладку «Приход» → «Добавить», выберите товар и укажите количество.");
        st.Bar.Children.Add(filter);
        st.Btn("Экспорт в Excel", () =>
        {
            var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"Остатки_{DateTime.Today:yyyyMMdd}.xlsx" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                Xlsx.Write(dlg.FileName, "Остатки", new[] { "Категория", "Наименование", "Ед.", "Приход", "Расход", "Остаток" },
                    ((List<Row>)st.G.ItemsSource).Select(r => r.V), 3, 4, 5);
                MessageBox.Show("Файл сохранён:\n" + dlg.FileName, "Экспорт в Excel");
            }
            catch (IOException) { H.Err("Не удалось сохранить файл. Возможно, он открыт в Excel — закройте его и повторите."); }
        });
        return st;
    }

    // ---------- Категории ----------
    Crud CatsTab() => new(
        new[] { "Название" },
        () => D.Categories.OrderBy(c => c.Name).Select(c => new Row { Obj = c, V = new[] { c.Name } }).ToList(),
        () => CatDlg(null),
        o => CatDlg((Category)o),
        o =>
        {
            var c = (Category)o;
            if (D.Items.Any(i => i.CategoryId == c.Id)) { H.Err("В категории есть номенклатура. Сначала удалите или перенесите её."); return; }
            if (!H.Yes($"Удалить категорию «{c.Name}»?")) return;
            D.Categories.Remove(c);
            foreach (var u in D.Users) u.CategoryIds.Remove(c.Id);
            Store.Save();
        },
        "Шаг 1. Создайте категории (например: «Картриджи», «Канцелярия»). Шаг 2 — добавьте в них товары на вкладке «Номенклатура». Шаг 3 — оформите «Приход», и появится остаток.");

    void CatDlg(Category? c)
    {
        var v = Dlg.Ask(c == null ? "Новая категория" : "Категория", new F("Название", c?.Name ?? ""));
        if (v == null) return;
        var n = v[0].Trim();
        if (n == "") return;
        if (D.Categories.Any(x => x != c && x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) { H.Err("Такая категория уже есть"); return; }
        if (c == null) D.Categories.Add(new Category { Name = n }); else c.Name = n;
        Store.Save();
    }

    // ---------- Номенклатура ----------
    Crud ItemsTab() => new(
        new[] { "Категория", "Наименование", "Ед. изм." },
        () => D.Items.OrderBy(i => H.Cat(i.CategoryId)).ThenBy(i => i.Name).Select(i => new Row { Obj = i, V = new[] { H.Cat(i.CategoryId), i.Name, i.Unit } }).ToList(),
        () => ItemDlg(null),
        o => ItemDlg((Item)o),
        o =>
        {
            var i = (Item)o;
            if (D.Receipts.Any(r => r.ItemId == i.Id) || D.Issues.Any(a => a.Lines.Any(l => l.ItemId == i.Id))) { H.Err("По товару есть движения (приход/выдача), удалить нельзя."); return; }
            if (H.Yes($"Удалить «{i.Name}»?")) { D.Items.Remove(i); Store.Save(); }
        },
        "Шаг 2. Нажмите «Добавить»: выберите категорию, введите название и единицу измерения (шт, кг, л, уп). Количество здесь НЕ вводится — оно добавляется на вкладке «Приход».");

    void ItemDlg(Item? it)
    {
        if (!D.Categories.Any()) { H.Err("Сначала создайте категорию"); return; }
        var cats = D.Categories.Select(c => c.Name).OrderBy(x => x).ToArray();
        var v = Dlg.Ask(it == null ? "Новый товар" : "Товар",
            new F("Категория", it == null ? cats[0] : H.Cat(it.CategoryId), cats),
            new F("Наименование", it?.Name ?? ""),
            new F("Единица измерения (шт, кг, л, уп — только слово, без цифр)", it?.Unit ?? "шт"));
        if (v == null || v[1].Trim() == "") return;
        if (v[2].Any(char.IsDigit)) { H.Err("Единица измерения — это слово (шт, кг, л, уп). Количество вводится на вкладке «Приход»."); return; }
        var cat = D.Categories.First(c => c.Name == v[0]);
        if (D.Items.Any(x => x != it && x.CategoryId == cat.Id && x.Name.Equals(v[1].Trim(), StringComparison.OrdinalIgnoreCase))) { H.Err("Такой товар уже есть в категории"); return; }
        it ??= new Item();
        bool isNew = !D.Items.Contains(it);
        if (isNew) D.Items.Add(it);
        it.CategoryId = cat.Id; it.Name = v[1].Trim(); it.Unit = v[2].Trim() == "" ? "шт" : v[2].Trim();
        Store.Save();
        if (isNew && H.Yes($"Товар «{it.Name}» добавлен, но остаток пока 0.\n\nОформить приход (поступление) прямо сейчас?"))
        {
            tc.SelectedIndex = 0;
            ReceiptDlg(null, it);
            (tc.SelectedContent as Crud)?.Refresh();
        }
    }

    // ---------- Пользователи ----------
    Crud UsersTab() => new(
        new[] { "Логин", "Пароль", "Имя", "Админ", "Доступ к выдаче" },
        () => D.Users.OrderBy(u => u.Login).Select(u => new Row
        {
            Obj = u,
            V = new[] { u.Login, u.Password == "" ? "(не задан — измените)" : u.Password, u.FullName, u.IsAdmin ? "да" : "", u.IsAdmin ? "все категории" : string.Join(", ", u.CategoryIds.Select(H.Cat)) }
        }).ToList(),
        () => UserDlg(null),
        o => UserDlg((User)o),
        o =>
        {
            var u = (User)o;
            if (u == Store.Cur) { H.Err("Нельзя удалить себя"); return; }
            if (H.Yes($"Удалить пользователя {u.Login}?")) { D.Users.Remove(u); Store.Save(); }
        },
        "Логины и пароли видны только администратору. Для сотрудника отметьте категории, из которых ему разрешено выдавать товар. «Изменить» — можно поменять и пароль.");

    void UserDlg(User? u)
    {
        var v = Dlg.Ask(u == null ? "Новый пользователь" : "Пользователь",
            new F("Логин", u?.Login ?? ""),
            new F("Имя", u?.FullName ?? ""),
            new F("Пароль", u?.Password ?? ""),
            new F("Администратор", u?.IsAdmin == true ? "Да" : "Нет", new[] { "Нет", "Да" }));
        if (v == null) return;
        var login = v[0].Trim();
        if (login == "") { H.Err("Укажите логин"); return; }
        if (D.Users.Any(x => x != u && x.Login.Equals(login, StringComparison.OrdinalIgnoreCase))) { H.Err("Логин занят"); return; }
        if (u == null && v[2] == "") { H.Err("Укажите пароль"); return; }
        bool adm = v[3] == "Да";
        if (!adm && !D.Users.Any(x => x != u && x.IsAdmin)) { H.Err("Должен остаться хотя бы один администратор"); return; }
        var cats = u?.CategoryIds.ToList() ?? new List<Guid>();
        if (!adm && D.Categories.Count > 0)
        {
            var cs = D.Categories.OrderBy(c => c.Name).ToList();
            var sel = Dlg.Pick("Доступ к выдаче", cs.Select(c => c.Name).ToArray(), cs.Select(c => cats.Contains(c.Id)).ToArray());
            if (sel == null) return;
            cats = cs.Where((c, i) => sel[i]).Select(c => c.Id).ToList();
        }
        if (u == null) { u = Store.NewUser(login, v[1].Trim(), v[2], adm); D.Users.Add(u); }
        else
        {
            u.Login = login; u.FullName = v[1].Trim();
            if (v[2] != "" && v[2] != u.Password) { u.Salt = Guid.NewGuid().ToString("N"); u.PwHash = Store.Pw(u.Salt, v[2]); u.Password = v[2]; }
        }
        u.IsAdmin = adm; u.CategoryIds = cats;
        Store.Save();
    }

    // ---------- Копии ----------
    UIElement BackupTab()
    {
        var sp = new StackPanel { Margin = new Thickness(14) };
        sp.Children.Add(new TextBlock
        {
            Text = $"Данные: {Store.DbPath}\nАвтокопия создаётся при каждом запуске (хранятся 30 последних):\n{Store.BackupDir}",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        Button B(string t, Action a)
        {
            var b = new Button { Content = t, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 0, 8) };
            b.Click += (_, _) => a(); sp.Children.Add(b); return b;
        }
        B("Создать копию…", () =>
        {
            var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = $"tmc_backup_{DateTime.Now:yyyyMMdd_HHmm}.json" };
            if (d.ShowDialog() != true) return;
            Store.Save(); File.Copy(Store.DbPath, d.FileName, true);
            MessageBox.Show("Копия сохранена");
        });
        B("Восстановить из копии…", () =>
        {
            var d = new OpenFileDialog { Filter = "JSON|*.json" };
            if (d.ShowDialog() != true) return;
            if (Store.Parse(d.FileName) is not { Users.Count: > 0 }) { H.Err("Файл не похож на копию базы"); return; }
            if (!H.Yes("Текущие данные будут заменены (перед этим сохранится автокопия). Продолжить?")) return;
            Store.AutoBackup();
            File.Copy(d.FileName, Store.DbPath, true);
            MessageBox.Show("Данные восстановлены. Программа закроется, запустите её снова.");
            Application.Current.Shutdown();
        });
        return sp;
    }
}
