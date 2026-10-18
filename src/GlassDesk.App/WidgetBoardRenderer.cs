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
    const float CardsTop = 132, BottomInset = 24, Radius = 20, Rim = .65f;
    const int FirstCardAction = 1100, CardActionStride = 80;

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
        // ContentHeight is the scrollable card extent, excluding the fixed bottom
        // window inset. The host may use ContentHeight - viewportHeight directly.
        float contentHeight = Math.Max(0, packing.ContentBottom - CardsTop - BottomInset);
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
        uint tone = highContrast ? (lightInk ? 0xff000000u : 0xffffffffu) : lightInk ? 0x0affffffu : 0x0a000000u;
        var panelBounds = new FloatRect(0, 0, width, panelHeight);

        // Toolbar controls have stable IDs independent of scrolling and card count.
        float available = width - 48, buttonWidth = Math.Min(60, Math.Max(0, (available - 8) / 2));
        float right = width - 24;
        float leftButton = right - buttonWidth * 2 - 8;
        if (leftButton > 112)
            toolbar.Add(Text("我的组件", TextRole.SectionTitle, new(24, 100, leftButton - 32, 24), primary, locale));
        if (editing)
        {
            ToolbarButton(1003, "完成", "完成组件布局编辑", WidgetBoardActionKind.Commit, leftButton, buttonWidth);
            ToolbarButton(1004, "取消", "取消组件布局编辑", WidgetBoardActionKind.Cancel, right - buttonWidth, buttonWidth);
        }
        else
        {
            ToolbarButton(1000, "添加", "添加组件", WidgetBoardActionKind.Add, leftButton, buttonWidth,
                board.Instances.Count < WidgetBoardData.MaximumInstances);
            ToolbarButton(1001, "编辑", "编辑组件布局", WidgetBoardActionKind.BeginEdit, right - buttonWidth, buttonWidth,
                board.Instances.Count > 0);
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
            CardSurface(cards, local, tone, lightInk, highContrast);

            if (editing)
            {
                cards.Add(Text(typeName, TextRole.WidgetLabel, new(local.X + 12, local.Y + 10, local.Width - 24, 18), primary, locale));
                float buttonSize = Math.Min(44, Math.Max(0, (local.Width - 28) / 2));
                float secondX = local.Right - 12 - buttonSize;
                EditButton(0, "配置", "\uE713", WidgetBoardActionKind.Configure, local.X + 12, local.Y + 38);
                EditButton(1, "移除", "\uE74D", WidgetBoardActionKind.Remove, secondX, local.Y + 38);
                EditButton(2, instance.Size == WidgetCardSize.Small ? "中卡" : "小卡", "\uE740", WidgetBoardActionKind.ToggleSize,
                    local.X + 12, local.Y + 92);
                EditButton(3, "拖动", "\uE700", WidgetBoardActionKind.DragHandle, secondX, local.Y + 92);

                void EditButton(int offset, string label, string glyph, WidgetBoardActionKind kind, float x, float y)
                {
                    var bounds = new FloatRect(x, y, buttonSize, 44);
                    cards.Add(Icon(glyph, new(x + (buttonSize - 16) / 2, y + 4, 16, 16), primary));
                    cards.Add(Text(label, TextRole.Caption, new(x, y + 23, buttonSize, 16), secondary, locale, true));
                    string verb = kind == WidgetBoardActionKind.ToggleSize ? $"切换为{label}" : label;
                    AddControl(actionBase + offset, $"{verb}{identity}", bounds with { Y = bounds.Y + CardsTop },
                        new(kind, instance.InstanceId), viewport);
                }
                continue;
            }

            bool failed = result?.State is WidgetProviderState.Unavailable or WidgetProviderState.Faulted or WidgetProviderState.TimedOut;
            bool hasSnapshot = result?.Snapshot?.Elements is { Count: > 0 };
            string contents = hasSnapshot ? string.Join("，", result!.Snapshot!.Elements.Where(e => e.Kind == WidgetElementKind.Text)
                .Take(64).Select(e => e.Text).Where(t => !string.IsNullOrWhiteSpace(t))) : "";
            // The entire card is the ordinary configuration target. Command targets
            // follow it so the host may prioritize the smaller, later hit regions.
            AddControl(actionBase, $"配置{identity}{(contents.Length > 0 ? "，" + contents : "")}", global,
                new(WidgetBoardActionKind.Configure, instance.InstanceId), viewport);
            cards.Add(Icon("\uE713", new(local.Right - 27, local.Y + 12, 14, 14), secondary));

            if (hasSnapshot)
            {
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
            cards.Add(Text("还没有组件", TextRole.SectionTitle, new(24, 24, width - 48, 26), primary, locale, true));
            cards.Add(Text("从添加开始，放入时间或日期", TextRole.Body, new(24, 62, width - 48, 24), secondary, locale, true));
        }
        if (!editing && addMenuOpen)
        {
            float menuWidth = Math.Min(152, width - 48), menuHeight = Math.Min(100, Math.Max(0, panelHeight - 140));
            var menu = new FloatRect(Math.Max(24, right - menuWidth * 1.35f), 132, menuWidth, menuHeight);
            // A short owned menu belongs to the toolbar layer above the card surface.
            CardSurface(toolbar, menu, highContrast ? tone : lightInk ? 0xf020252c : 0xf0f4f5f7, lightInk, highContrast);
            MenuEntry(1005, "时间", WidgetBoardActionKind.AddTime, menu.Y + 4);
            MenuEntry(1006, "日期", WidgetBoardActionKind.AddDate, menu.Y + 50);

            void MenuEntry(int id, string label, WidgetBoardActionKind kind, float y)
            {
                var bounds = Intersect(new(menu.X + 4, y, menu.Width - 8, 44), menu);
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
                toolbar.Add(Text(label, TextRole.Label, bounds, primary, locale, true));
                AddControl(id, $"添加{label}组件", bounds, new(kind), panelBounds,
                    board.Instances.Count < WidgetBoardData.MaximumInstances);
            }
        }
        return new(Array.AsReadOnly(toolbar.ToArray()), Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(controls.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, WidgetBoardUiAction>(actions), packing, scrollOffset, contentHeight);

        void ToolbarButton(int id, string label, string name, WidgetBoardActionKind kind, float x, float targetWidth, bool enabled = true)
        {
            var bounds = Intersect(new(x, 88, targetWidth, 44), panelBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            toolbar.Add(Text(label, TextRole.Label, bounds, enabled ? primary : secondary, locale, true));
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

    static string Name(WidgetInstance instance) => instance.ProviderId switch
    { BuiltInWidgetIds.Time => "时间", BuiltInWidgetIds.Date => "日期", _ => "本地" };
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
    static void CardSurface(List<SurfaceOp> ops, FloatRect b, uint tone, bool lightInk, bool highContrast)
    {
        if (b.Width <= 0 || b.Height <= 0) return;
        float radius = Math.Min(Radius, Math.Min(b.Width, b.Height) / 2), inset = Rim / 2;
        ops.Add(Round(b, radius, tone));
        uint upper = highContrast ? (lightInk ? 0xffffffff : 0xff000000) : 0x42ffffff;
        uint lower = highContrast ? upper : lightInk ? 0x10ffffffu : 0x14000000u;
        float stroke = highContrast ? 1 : Rim;
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
