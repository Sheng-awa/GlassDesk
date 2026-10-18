using System.Globalization;
using GlassDesk.Core;
using GlassDesk.NativeBridge;

namespace GlassDesk.App;

internal enum WidgetBoardActionKind
{
    Add, AddTime, AddDate, BeginEdit, Commit, Cancel, Configure, Remove, ToggleSize, DragHandle, InvokeCommand
}

internal sealed record WidgetBoardUiAction(WidgetBoardActionKind Kind, string? InstanceId = null, string? CommandId = null);

internal sealed record WidgetBoardRenderResult(
    IReadOnlyList<SurfaceOp> ToolbarOperations,
    IReadOnlyList<SurfaceOp> CardOperations,
    IReadOnlyList<AccessibleControl> Controls,
    IReadOnlyDictionary<int, WidgetBoardUiAction> Actions,
    WidgetBoardPacking Packing,
    float ScrollOffset,
    float ContentHeight);

/// <summary>
/// Stateless, viewport-sized paint and input description. The host owns surfaces,
/// scrolling, provider scheduling and edit transactions. Provider results must have
/// passed WidgetBoardScheduler validation; this adapter additionally bounds every
/// element to the actual (possibly single-column) card width.
/// </summary>
internal static class WidgetBoardRenderer
{
    // Cards start below the 58–88 toolbar row and the bottom inset keeps them clear of the Dock.
    const float CardsTop = WidgetBoardLayout.Top, BottomInset = WidgetBoardLayout.BottomInset, Radius = 16, Rim = .75f;
    // Title 16/24 at y=62; ghost 编辑 and 30-high accent 添加 are flush right at y=58.
    // Icon-only toolbar at y=58; the add control is one 32 DIP accent circle, edit is a ghost icon.
    const float ToolbarTop = 58, ButtonGap = 8;
    const float AddMenuRowHeight = 44, AddMenuWidth = 152, AddMenuRadius = 12;
    const uint AccentFill = 0xff1689f9;
    const int FirstCardAction = 1100, CardActionStride = 80;

    /// <summary>Whole-card controls need the 16 DIP card radius for their press layer, never a pill radius.</summary>
    internal static bool IsCardPressTarget(AccessibleControl control) =>
        control.Id >= FirstCardAction && (control.Id - FirstCardAction) % CardActionStride == 0 && control.Width > 60;

    public static WidgetBoardRenderResult Build(WidgetBoardState board, IReadOnlyList<WidgetInstanceResult> results,
        float width, float panelHeight, float scrollOffset, bool editing, bool addMenuOpen, bool lightInk,
        string locale, bool highContrast = false)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (!float.IsFinite(width) || width <= WidgetBoardLayout.Left * 2 || !float.IsFinite(panelHeight) || panelHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        board = WidgetBoardData.ValidateAndClone(board);
        var packing = WidgetBoardLayout.Pack(board, width, CardsTop);
        float viewportHeight = Math.Max(0, panelHeight - CardsTop - BottomInset);
        // ContentHeight is the card extent measured from the viewport top, including the
        // packing's trailing 24 DIP margin. The host derives the scroll range with
        // ContentHeight - viewportHeight, so the last row can clear the dock inset.
        float contentHeight = Math.Max(0, packing.ContentBottom - CardsTop);
        float maximumScroll = Math.Max(0, contentHeight - viewportHeight);
        scrollOffset = float.IsFinite(scrollOffset) ? Math.Clamp(scrollOffset, 0, maximumScroll) : 0;
        var viewport = new FloatRect(0, CardsTop, width, viewportHeight);
        var toolbar = new List<SurfaceOp>();
        var cards = new List<SurfaceOp>();
        var controls = new List<AccessibleControl>();
        var actions = new Dictionary<int, WidgetBoardUiAction>();
        uint primary = TypographyCatalog.Ink(InkRole.Primary, lightInk);
        uint secondary = TypographyCatalog.Ink(InkRole.Secondary, lightInk);
        if (highContrast) primary = secondary = lightInk ? 0xffffffff : 0xff000000;
        // A paper-thin card face keeps the glass visible: white 8% on light material and
        // black 7% on dark material, with a stronger upper-left rim for the raised edge.
        uint tone = highContrast ? (lightInk ? 0xff000000u : 0xffffffffu) : lightInk ? 0x12000000u : 0x14ffffffu;
        var panelBounds = new FloatRect(0, 0, width, panelHeight);
        int instanceCount = board.Instances.Count;

        // Icon-only toolbar: a plus and a pencil, no title. The active control keeps one accent
        // circle so the page still reads as a compact tool surface rather than a text header.
        float right = width - 24;
        const float toolbarButtonSize = 32;
        float addX = right - toolbarButtonSize;
        float editX = addX - ButtonGap - toolbarButtonSize;
        if (editing)
        {
            ToolbarIconButton(1003, "\uE73E", "完成组件布局编辑", WidgetBoardActionKind.Commit, addX, accent: true);
            ToolbarIconButton(1004, "\uE711", "取消组件布局编辑", WidgetBoardActionKind.Cancel, editX, accent: false);
        }
        else
        {
            ToolbarIconButton(1000, "\uE710", "添加组件", WidgetBoardActionKind.Add, addX, accent: true,
                instanceCount < WidgetBoardData.MaximumInstances);
            ToolbarIconButton(1001, "\uE70F", "编辑组件布局", WidgetBoardActionKind.BeginEdit, editX, accent: false,
                instanceCount > 0);
        }

        var resultById = results.Where(r => r is not null).GroupBy(r => r.InstanceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Generation).First(), StringComparer.Ordinal);
        var instanceById = board.Instances.ToDictionary(i => i.InstanceId, StringComparer.Ordinal);
        var eligible = WidgetBoardLayout.EligibleInstances(packing, new(0, CardsTop + scrollOffset, width, viewportHeight));
        for (int index = 0; index < packing.Cards.Count; index++)
        {
            var placement = packing.Cards[index];
            if (!eligible.Contains(placement.InstanceId)) continue;
            var instance = instanceById[placement.InstanceId];
            var local = placement.Bounds with { Y = placement.Bounds.Y - CardsTop - scrollOffset };
            var global = local with { Y = local.Y + CardsTop };
            int actionBase = FirstCardAction + index * CardActionStride;
            string typeName = Name(instance), identity = $"{typeName}组件，第 {index + 1} 个";
            resultById.TryGetValue(instance.InstanceId, out var result);
            bool failed = result?.State is WidgetProviderState.Unavailable or WidgetProviderState.Faulted or WidgetProviderState.TimedOut;
            bool hasSnapshot = result?.Snapshot?.Elements is { Count: > 0 };
            bool isTime = instance.ProviderId == BuiltInWidgetIds.Time;
            // A plain time card: one quiet number, no label and no caption. Editing and
            // fallback states still share the card face.
            CardSurface(cards, local, tone, lightInk, highContrast, editing);

            if (editing)
            {
                // The compact edit state keeps one title line, a top-right drag handle and a
                // bottom row of three 44 DIP icon buttons instead of a 2x2 block.
                cards.Add(Text(typeName, TextRole.WidgetLabel,
                    new(local.X + 12, local.Y + 10, Math.Max(0, local.Width - 80), 18), primary, locale));
                float buttonSize = 44;
                float rowX = local.X + (local.Width - buttonSize * 3) / 2;
                float rowY = local.Bottom - 12 - buttonSize;
                EditButton(0, "配置", "\uE713", WidgetBoardActionKind.Configure, rowX, rowY);
                EditButton(1, instance.Size == WidgetCardSize.Small ? "中卡" : "小卡", "\uE740",
                    WidgetBoardActionKind.ToggleSize, rowX + buttonSize, rowY);
                EditButton(2, "移除", "\uE74D", WidgetBoardActionKind.Remove, rowX + buttonSize * 2, rowY);
                EditButton(3, "拖动", "\uE700", WidgetBoardActionKind.DragHandle, local.Right - 12 - buttonSize, local.Y + 6);
                continue;

                void EditButton(int offset, string label, string glyph, WidgetBoardActionKind kind, float x, float y)
                {
                    var bounds = new FloatRect(x, y, buttonSize, buttonSize);
                    cards.Add(Icon(glyph, new(x + (buttonSize - 16) / 2, y + (buttonSize - 16) / 2, 16, 16), primary));
                    string verb = kind == WidgetBoardActionKind.ToggleSize ? $"切换为{label}" : label;
                    AddControl(actionBase + offset, $"{verb}{identity}", bounds with { Y = bounds.Y + CardsTop },
                        new(kind, instance.InstanceId), viewport);
                }
            }

            string contents = hasSnapshot ? string.Join("，", result!.Snapshot!.Elements.Where(e => e.Kind == WidgetElementKind.Text)
                .Take(64).Select(e => e.Text).Where(t => !string.IsNullOrWhiteSpace(t))) : "";
            // The entire card is the ordinary configuration target. Command targets
            // follow it so the host may prioritize the smaller, later hit regions.
            AddControl(actionBase, $"配置{identity}{(contents.Length > 0 ? "，" + contents : "")}", global,
                new(WidgetBoardActionKind.Configure, instance.InstanceId), viewport);

            if (isTime)
            {
                if (!hasSnapshot || !TryClock(result!, out int hour, out int minute, out string display))
                {
                    // The watch face can draw the local clock immediately; a provider snapshot
                    // later wins when the user configured another time zone.
                    var now = DateTime.Now;
                    hour = now.Hour; minute = now.Minute; display = now.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
                WatchFace(cards, local, hour, minute, display, locale);
            }
            else if (hasSnapshot)
            {
                cards.Add(Icon("\uE713", new(local.Right - 27, local.Y + 12, 14, 14), secondary));
                int elementIndex = 0;
                foreach (var element in result!.Snapshot!.Elements.Take(64))
                {
                    int commandId = actionBase + 10 + elementIndex++;
                    if (!Finite(element.Bounds)) continue;
                    var elementBounds = Intersect(element.Bounds, new(0, 0, local.Width, local.Height));
                    // Reserve only the title band for the normal configuration mark.
                    if (elementBounds.Y < 30) elementBounds = Intersect(elementBounds, new(0, 0, Math.Max(0, local.Width - 36), local.Height));
                    if (failed && elementBounds.Bottom > local.Height - 32)
                        elementBounds = Intersect(elementBounds, new(0, 0, Math.Max(0, local.Width - 36), local.Height));
                    if (elementBounds.Width <= 0 || elementBounds.Height <= 0) continue;
                    var paintBounds = elementBounds with { X = local.X + elementBounds.X, Y = local.Y + elementBounds.Y };
                    switch (element.Kind)
                    {
                        case WidgetElementKind.Text when element.Text is not null && TypographyCatalog.All.ContainsKey(element.TextRole):
                            var numeric = element.NumericStyle switch
                            {
                                WidgetNumericStyle.Tabular => TextFlags.TabularNumbers,
                                WidgetNumericStyle.DigitsOnly => TextFlags.TabularDigitsOnly,
                                _ => TextFlags.None
                            };
                            bool value = element.TextRole is TextRole.WidgetClock or TextRole.WidgetDateValue or TextRole.WidgetValue
                                or TextRole.MediaDetailsTitle or TextRole.PageTitle;
                            cards.Add(Text(element.Text, element.TextRole, paintBounds, value ? primary : secondary, locale, numeric: numeric));
                            break;
                        case WidgetElementKind.Symbol when Symbol(element.SymbolKey) is { } symbol:
                            cards.Add(Icon(symbol, paintBounds, secondary));
                            break;
                        case WidgetElementKind.Separator:
                            cards.Add(Line(paintBounds.X, paintBounds.Y + paintBounds.Height / 2, paintBounds.Width, 0,
                                highContrast ? primary : WithAlpha(secondary, 0x38), highContrast ? 1 : Rim));
                            break;
                        case WidgetElementKind.ProgressValue when element.Value is { } progress && double.IsFinite(progress):
                            float height = Math.Min(4, paintBounds.Height);
                            float y = paintBounds.Y + (paintBounds.Height - height) / 2;
                            cards.Add(Round(new(paintBounds.X, y, paintBounds.Width, height), height / 2,
                                highContrast ? primary : WithAlpha(secondary, 0x30)));
                            cards.Add(Round(new(paintBounds.X, y, paintBounds.Width * (float)Math.Clamp(progress, 0, 1), height), height / 2, primary));
                            break;
                    }
                    if (WidgetBoardData.IsIdentifier(element.CommandId))
                        AddControl(commandId, $"{element.Text ?? element.SymbolKey ?? "执行操作"}，{identity}",
                            paintBounds with { Y = paintBounds.Y + CardsTop },
                            new(WidgetBoardActionKind.InvokeCommand, instance.InstanceId, element.CommandId), viewport);
                }
            }
            else
            {
                cards.Add(Text(typeName, TextRole.WidgetLabel, new(local.X + 12, local.Y + 12, local.Width - 48, 16), secondary, locale));
                cards.Add(Text(failed ? "组件暂不可用" : "正在准备组件", TextRole.Body,
                    new(local.X + 12, local.Y + 52, local.Width - 24, 24), primary, locale));
                cards.Add(Text(failed ? "配置仍保留" : "即将显示当前信息", TextRole.Caption,
                    new(local.X + 12, local.Y + 88, local.Width - 24, 18), secondary, locale));
            }
            if (failed && hasSnapshot)
            {
                // Preserve the successful snapshot. A small status mark plus the
                // configuration control's full name exposes the stale state.
                cards.Add(Icon("\uE7BA", new(local.Right - 27, local.Bottom - 26, 14, 14), secondary));
                int controlIndex = controls.FindIndex(c => c.Id == actionBase);
                if (controlIndex >= 0) controls[controlIndex] = controls[controlIndex] with
                { Name = controls[controlIndex].Name + "，更新失败，当前保留上次成功内容" };
            }
        }

        if (packing.Cards.Count == 0 && viewportHeight > 0)
        {
            // Empty state is vertically centered in the card viewport: 32 DIP trough,
            // title, caption and an accent add button share one 144 DIP block.
            float top = Math.Max(16, (viewportHeight - 144) / 2), center = width / 2;
            cards.Add(Round(new(center - 16, top, 32, 32), 16,
                highContrast ? WithAlpha(primary, 0x48) : lightInk ? 0x1fffffffu : 0x1020252cu));
            cards.Add(Text("还没有组件", TextRole.SectionTitle, new(24, top + 48, width - 48, 24), primary, locale, true));
            cards.Add(Text("添加时间或日期开始", TextRole.Caption, new(24, top + 76, width - 48, 16), secondary, locale, true));
            var emptyAdd = new FloatRect(center - 60, top + 112, 120, 32);
            bool emptyEnabled = instanceCount < WidgetBoardData.MaximumInstances;
            cards.Add(Round(emptyAdd, emptyAdd.Height / 2, AccentFace(emptyEnabled, lightInk, highContrast)));
            cards.Add(Text("添加组件", TextRole.Label, emptyAdd, AccentInk(emptyEnabled, lightInk, highContrast), locale, true));
            AddControl(1007, "添加组件", emptyAdd with { Y = emptyAdd.Y + CardsTop },
                new(WidgetBoardActionKind.Add), viewport, emptyEnabled);
        }
        if (!editing && addMenuOpen)
        {
            // A short light flyout belongs to the toolbar layer above the card surface;
            // its right edge lines up with the 添加 button.
            float menuWidth = Math.Min(AddMenuWidth, width - 48);
            var menu = new FloatRect(right - menuWidth, ToolbarTop + 32 + 4, menuWidth, AddMenuRowHeight + 8);
            uint menuFill = highContrast ? (lightInk ? 0xff000000u : 0xffffffffu) : 0xfff8fafcu;
            uint menuInk = highContrast ? (lightInk ? 0xffffffffu : 0xff000000u) : 0xff20252cu;
            toolbar.Add(Round(menu, AddMenuRadius, menuFill));
            MenuEntry(1005, "时间", "\uE917", WidgetBoardActionKind.AddTime, menu.Y + 4);

            void MenuEntry(int id, string label, string glyph, WidgetBoardActionKind kind, float y)
            {
                var bounds = Intersect(new(menu.X + 4, y, menu.Width - 8, AddMenuRowHeight), menu);
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
                toolbar.Add(Icon(glyph, new(bounds.X + 12, bounds.Y + 14, 16, 16), menuInk));
                toolbar.Add(Text(label, TextRole.Label, new(bounds.X + 40, bounds.Y + 12, bounds.Width - 48, 20), menuInk, locale));
                AddControl(id, $"添加{label}组件", bounds, new(kind), panelBounds,
                    instanceCount < WidgetBoardData.MaximumInstances);
            }
        }
        return new(Array.AsReadOnly(toolbar.ToArray()), Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(controls.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, WidgetBoardUiAction>(actions), packing, scrollOffset, contentHeight);

        void ToolbarIconButton(int id, string glyph, string name, WidgetBoardActionKind kind, float x,
            bool accent, bool enabled = true)
        {
            var bounds = Intersect(new(x, ToolbarTop, toolbarButtonSize, toolbarButtonSize), panelBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (accent) toolbar.Add(Round(bounds, toolbarButtonSize / 2, AccentFace(enabled, lightInk, highContrast)));
            toolbar.Add(Icon(glyph, new(bounds.X + (bounds.Width - 16) / 2, bounds.Y + (bounds.Height - 16) / 2, 16, 16),
                accent ? AccentInk(enabled, lightInk, highContrast) : enabled ? primary : secondary));
            AddControl(id, name, bounds, new(kind), panelBounds, enabled);
        }
        void AddControl(int id, string name, FloatRect bounds, WidgetBoardUiAction action, FloatRect clip, bool enabled = true)
        {
            bounds = Intersect(bounds, clip);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            controls.Add(new(id, name, bounds.X, bounds.Y, bounds.Width, bounds.Height, enabled));
            actions.Add(id, action);
        }
    }

    static uint AccentFace(bool enabled, bool lightInk, bool highContrast)
    {
        if (highContrast) return enabled ? (lightInk ? 0xffffffffu : 0xff000000u) : (lightInk ? 0x66ffffffu : 0x66000000u);
        return enabled ? AccentFill : 0x301689f9u;
    }
    static uint AccentInk(bool enabled, bool lightInk, bool highContrast)
    {
        if (highContrast) return enabled ? (lightInk ? 0xff000000u : 0xffffffffu) : (lightInk ? 0xff84909cu : 0xff8e98a3u);
        return enabled ? 0xfff8fafcu : (lightInk ? 0xff84909cu : 0xff8e98a3u);
    }

    static string Name(WidgetInstance instance) => instance.ProviderId switch
    { BuiltInWidgetIds.Time => "时间", BuiltInWidgetIds.Date => "日期", _ => "本地" };

    /// <summary>Reads the visible HH:mm value from the provider snapshot (12h suffixes are ignored).</summary>
    static bool TryClock(WidgetInstanceResult result, out int hour, out int minute, out string display)
    {
        hour = minute = 0; display = string.Empty;
        var elements = result.Snapshot?.Elements;
        if (elements is not { Count: > 0 }) return false;
        var text = elements.FirstOrDefault(e => e.Kind == WidgetElementKind.Text
            && e.TextRole is TextRole.WidgetClock or TextRole.MediaDetailsTitle)?.Text
            ?? elements.FirstOrDefault(e => e.Kind == WidgetElementKind.Text)?.Text;
        if (string.IsNullOrWhiteSpace(text)) return false;
        int colon = text.IndexOf(':');
        if (colon < 1 || colon + 2 >= text.Length) return false;
        if (!int.TryParse(text.AsSpan(0, colon).Trim(), out hour)) return false;
        if (!int.TryParse(text.AsSpan(colon + 1, 2), out minute)) return false;
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        display = $"{hour:00}:{minute:00}";
        return true;
    }

    /// <summary>Apple-Watch-style dial: white face, minute ticks, black hour/minute hands,
    /// a thin red second hand and a small digital time where the watch logo would sit.</summary>
    static void WatchFace(List<SurfaceOp> cards, FloatRect local, int hour, int minute, string display, string locale)
    {
        float radius = Math.Min(local.Width, local.Height) / 2 - 10;
        float cx = local.X + local.Width / 2, cy = local.Y + local.Height / 2 + 2;
        const uint ink = 0xFF1A1C1F, tick = 0xB020252C, strongTick = 0xE020252C, red = 0xFFE5484D;
        // Soft lifted shadow, then the white dial. Both are plain ellipses so the face reads
        // as a watch crystal on top of the glass rather than a painted square card.
        cards.Add(new SurfaceOp
        {
            Kind = SurfaceOpKind.Ellipse, X = cx - radius - 1.5f, Y = cy - radius + 1.5f,
            Width = (radius + 1.5f) * 2, Height = (radius + 1.5f) * 2, Color = 0x1A000000
        });
        cards.Add(new SurfaceOp
        {
            Kind = SurfaceOpKind.Ellipse, X = cx - radius, Y = cy - radius,
            Width = radius * 2, Height = radius * 2, Color = 0xF5FFFFFF
        });
        // 60 minute ticks; every fifth tick is slightly longer and darker.
        for (int i = 0; i < 60; i++)
        {
            float a = i * MathF.Tau / 60;
            float outer = radius - 3;
            float inner = outer - (i % 5 == 0 ? 5.5f : 3f);
            float sx = cx + MathF.Sin(a) * inner, sy = cy - MathF.Cos(a) * inner;
            float ex = cx + MathF.Sin(a) * outer, ey = cy - MathF.Cos(a) * outer;
            cards.Add(Line(sx, sy, ex - sx, ey - sy, i % 5 == 0 ? strongTick : tick, i % 5 == 0 ? 1.2f : .8f));
        }
        float hourAngle = (hour % 12 + minute / 60f) * MathF.PI / 6;
        float minuteAngle = minute * MathF.PI / 30;
        float secondAngle = DateTime.Now.Second * MathF.PI / 30;
        AddHand(hourAngle, radius * .48f, 4.2f, 0, ink);
        AddHand(minuteAngle, radius * .70f, 2.8f, 0, ink);
        AddHand(secondAngle, radius * .78f, 1.2f, radius * .16f, red);
        cards.Add(new SurfaceOp
        {
            Kind = SurfaceOpKind.Ellipse, X = cx - 4, Y = cy - 4, Width = 8, Height = 8, Color = ink
        });
        cards.Add(new SurfaceOp
        {
            Kind = SurfaceOpKind.Ellipse, X = cx - 1.6f, Y = cy - 1.6f, Width = 3.2f, Height = 3.2f, Color = red
        });
        // Small digital time at 12 o'clock, replacing the Apple logo of the reference.
        var digital = SurfaceOp.Typography(display, TextRole.NavigationSemantic,
            cx - 30, cy - radius + 13, 60, 14, ink, center: true,
            numericFlags: TextFlags.TabularNumbers, locale: locale);
        cards.Add(digital with { FontSize = 9.5f, LineHeightDip = 12f });

        void AddHand(float angle, float length, float stroke, float tail, uint color)
        {
            float dx = MathF.Sin(angle), dy = -MathF.Cos(angle);
            cards.Add(Line(cx - dx * tail, cy - dy * tail, dx * (length + tail), dy * (length + tail), color, stroke));
        }
    }

    static bool Finite(FloatRect b) => float.IsFinite(b.X) && float.IsFinite(b.Y) && float.IsFinite(b.Width)
        && float.IsFinite(b.Height) && float.IsFinite(b.Right) && float.IsFinite(b.Bottom) && b.Width >= 0 && b.Height >= 0;
    static FloatRect Intersect(FloatRect a, FloatRect b)
    {
        float x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }
    static uint WithAlpha(uint color, byte alpha) => (color & 0x00ffffff) | ((uint)alpha << 24);
    static SurfaceOp Text(string text, TextRole role, FloatRect b, uint ink, string locale, bool center = false,
        TextFlags numeric = TextFlags.None) => SurfaceOp.Typography(text, role, b.X, b.Y, b.Width, b.Height, ink,
            center, numeric, locale);
    static SurfaceOp Round(FloatRect b, float radius, uint color) => new()
    { Kind = SurfaceOpKind.RoundedRectangle, X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, Radius = radius, Color = color };
    static SurfaceOp Line(float x, float y, float width, float height, uint color, float stroke = Rim) => new()
    { Kind = SurfaceOpKind.Line, X = x, Y = y, Width = width, Height = height, Radius = stroke, Color = color };
    static SurfaceOp Icon(string glyph, FloatRect b, uint ink) => new()
    {
        Kind = SurfaceOpKind.Icon, Text = glyph, X = b.X, Y = b.Y, Width = b.Width, Height = b.Height,
        FontSize = Math.Min(20, Math.Min(b.Width, b.Height)), Color = ink, Center = true
    };
    static string? Symbol(string? key) => key switch
    {
        "clock" => "\uE917", "calendar" => "\uE787", "settings" => "\uE713", "info" => "\uE946",
        "warning" => "\uE7BA", "check" => "\uE73E", "heart" => "\uEB51", "music" => "\uE8D6", _ => null
    };
    static void CardSurface(List<SurfaceOp> ops, FloatRect b, uint tone, bool lightInk, bool highContrast, bool editing = false)
    {
        if (b.Width <= 0 || b.Height <= 0) return;
        float radius = Math.Min(Radius, Math.Min(b.Width, b.Height) / 2);
        if (editing)
        {
            // Edit mode keeps one deepened face and adds only a 1px accent ring, so the
            // card reads as selected without turning the whole surface accent.
            uint face = highContrast ? tone : lightInk ? 0x2a000000u : 0x2220252cu;
            uint accent = highContrast ? (lightInk ? 0xffffffffu : 0xff000000u) : AccentFill;
            ops.Add(Round(b, radius, face));
            Ring(ops, b, radius, accent, accent, 1);
            return;
        }
        ops.Add(Round(b, radius, tone));
        uint upper = highContrast ? (lightInk ? 0xffffffffu : 0xff000000u) : 0x42ffffffu;
        uint lower = highContrast ? upper : 0x14ffffffu;
        Ring(ops, b, radius, upper, lower, highContrast ? 1 : Rim);
    }
    // Four edge lines plus four corner arcs; shared by the normal rim and the edit accent ring.
    static void Ring(List<SurfaceOp> ops, FloatRect b, float radius, uint upper, uint lower, float stroke)
    {
        float inset = stroke / 2;
        ops.Add(Line(b.X + radius, b.Y + inset, b.Width - radius * 2, 0, upper, stroke));
        ops.Add(Line(b.X + inset, b.Y + radius, 0, b.Height - radius * 2, upper, stroke));
        ops.Add(Line(b.X + radius, b.Bottom - inset, b.Width - radius * 2, 0, lower, stroke));
        ops.Add(Line(b.Right - inset, b.Y + radius, 0, b.Height - radius * 2, lower, stroke));
        Arc(b.X + radius, b.Y + radius, MathF.PI, upper);
        Arc(b.Right - radius, b.Y + radius, MathF.PI * 1.5f, upper);
        Arc(b.Right - radius, b.Bottom - radius, 0, lower);
        Arc(b.X + radius, b.Bottom - radius, MathF.PI * .5f, lower);
        void Arc(float cx, float cy, float start, uint ink)
        {
            float r = Math.Max(0, radius - inset);
            for (int segment = 0; segment < 5; segment++)
            {
                float a = start + segment * MathF.PI / 10, z = a + MathF.PI / 10;
                float x = cx + MathF.Cos(a) * r, y = cy + MathF.Sin(a) * r;
                ops.Add(Line(x, y, cx + MathF.Cos(z) * r - x, cy + MathF.Sin(z) * r - y, ink, stroke));
            }
        }
    }
}
