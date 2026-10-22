using GlassDesk.App;
using GlassDesk.Core;

static class HomePopupChecks
{
    public static void Run(Action<bool, string> check)
    {
        var layout = HomePresentationLayout.Resolve(360, 512);
        var entries = new List<HomeFolderEntry> { new("before.txt", "C:/test/before.txt", false) };
        var preview = new HomeFolderPreview("test", "C:/test", entries);
        HomePopupRenderKey Key(HomeFolderPreview value) => HomePopupRenderKey.Create(layout, 0, value,
            -1, 0xFF20252C, 0xFFF5F7FA, 0xFF48515B, 144, false);
        var original = Key(preview);
        check(original == Key(preview with { Entries = entries.ToArray() }),
            "Home popup reuses equal directory content across different collection instances");
        entries[0] = new("after.txt", "C:/test/after.txt", false);
        var changed = Key(preview);
        check(original != changed && original.First?.Name == "before.txt",
            "Home popup snapshots entries so renames and mutable-list replacement repaint the displayed actions");
        check(changed != Key(preview with { Entries = [new("after.txt", "C:/else/after.txt", false)] })
            && changed != Key(preview with { Entries = [new("after.txt", "C:/test/after.txt", true)] }),
            "Home popup cache tracks entry paths and file-versus-folder glyphs");
        check(changed != Key(preview with { Entries = [] })
            && changed != Key(preview with { Status = HomeFolderStatus.Error, Message = "denied" }),
            "Home popup invalidates on empty and error transitions");
        check(changed != changed with { Secondary = 0xFFFFFFFF }
            && changed != changed with { HighContrast = true }
            && changed != changed with { Dpi = 192 },
            "Home popup theme, secondary ink and DPI transitions invalidate the cached surface");
    }
}
