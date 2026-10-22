using GlassDesk.Core;

static class HomePresentationChecks
{
    public static void Run(Action<bool,string> check)
    {
        var standard = HomePresentationLayout.Resolve(360,512);
        check(standard.NavCard == HomeLayout.NavCard && standard.Dock == HomeLayout.Dock,
            "Home viewport layout preserves the reviewed 360x512 baseline");
        bool docks=true, targets=true, popups=true, reachable=true, icons=true;
        foreach(int width in new[]{320,360})
        foreach(int height in new[]{300,360,400,512})
        {
            var layout=HomePresentationLayout.Resolve(width,height);
            docks &= layout.Dock.X>=0 && layout.Dock.Right<=width && layout.Dock.Bottom<=height;
            for(int i=0;i<3;i++)
            {
                var target=layout.NavEntry(i);
                targets &= target.Width>=44 && target.Height>=44;
                for(int j=i+1;j<3;j++)targets &= !target.Intersects(layout.NavEntry(j));
                var scrolled=HomePresentationLayout.Resolve(width,height,layout.MaxScroll);
                reachable &= scrolled.VisibleTarget(scrolled.NavEntry(i)) is not null;
                var preview=layout.Preview(i);
                popups &= preview.Height>=44 && preview.Bottom<layout.PopoverOpenButton.Y;
            }
            popups &= layout.Popover.Y>=48 && layout.Popover.Bottom<layout.Dock.Y;
            for(int i=0;i<6;i++)icons &= layout.AppIcon(i).X>=0 && layout.AppIcon(i).Right<=width;
        }
        check(docks,"Home folder dock stays on screen in short and narrow work areas");
        check(targets,"Home semantic navigation targets are 44 DIP and never overlap");
        check(popups,"Home compact popover keeps readable previews above its fixed action");
        check(reachable,"Home scrolling can reveal every navigation action in short work areas");
        check(icons,"Home app icons fit both 320 and 360 DIP widths");
    }
}

