using GlassDesk.Core;

static class OutsidePointerChecks
{
    public static void Run(Action<bool,string> check)
    {
        var popup=new List<FloatRect>{new(500,100,80,40)};
        var original=new OutsidePointerSnapshot(7,1,new(100,100,360,476),24,ownedPopupRegions:popup);
        popup.Clear();
        check(original.Contains(280,110)&&!original.Contains(101,101)&&original.IsOutsidePress(99,120),"outside observation uses the rounded visible shell and excludes shadow");
        check(original.Contains(520,120),"owned popup geometry is an immutable input snapshot");
        var moved=new OutsidePointerSnapshot(7,2,new(900,400,540,714),36);
        check(original.IsOutsidePress(950,500)&&!moved.IsOutsidePress(950,500),"movement and DPI scaling preserve the geometry from each event time");
        var press=new OutsidePointerPress(7,1,950,500,OutsidePointerButton.Middle,true);
        check(OutsidePointerSnapshot.BelongsToVisibleDetails(press,7,true,WidgetForm.Details)&&
            !OutsidePointerSnapshot.BelongsToVisibleDetails(press,8,true,WidgetForm.Details)&&
            !OutsidePointerSnapshot.BelongsToVisibleDetails(press,7,false,WidgetForm.Details),"queued injected or middle clicks retain event identity and cannot close a newer open cycle");
        foreach(var capture in new[]{PointerCaptureOwner.WidgetDrag,PointerCaptureOwner.Timeline,PointerCaptureOwner.Navigation,PointerCaptureOwner.CompanionReveal,PointerCaptureOwner.WidgetBoard})
            check(!new OutsidePointerSnapshot(7,3,new(100,100,360,476),24,capture).IsOutsidePress(0,0),"captured "+capture+" owns input outside the shell");
        check(!new OutsidePointerSnapshot(7,4,new(0,0,float.NaN,300),24).IsOutsidePress(0,0),"invalid geometry cannot cause an outside collapse");
    }
}
