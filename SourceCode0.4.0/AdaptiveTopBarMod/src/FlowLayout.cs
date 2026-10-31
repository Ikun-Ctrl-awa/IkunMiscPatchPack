namespace AdaptiveTopBar;

public readonly record struct FlowItem(float Width,float Height,float GapBefore=12,bool Right=false);
public readonly record struct Placement(int Index,float X,float Y,float Width,float Height,float Scale,int Row);
public sealed record FlowResult(Placement[] Items,int Rows,float Height);
public static class FlowLayout {
 // 纯布局计算：保留顺序，不裁剪控件，不限制为二行或三行。
 public static FlowResult Pack(float width,IReadOnlyList<FlowItem> items,float padding=16,float rowHeight=80,float rowGap=4){
  if(!float.IsFinite(width)||width<=2*padding)throw new ArgumentOutOfRangeException(nameof(width));
  float available=width-2*padding;var result=new List<Placement>();int row=0;float x=padding,y=0,h=rowHeight;
  for(int i=0;i<items.Count;i++){
   var item=items[i];if(!float.IsFinite(item.Width)||!float.IsFinite(item.Height)||item.Width<0||item.Height<0)throw new ArgumentException("Invalid item size");
   float scale=item.Width>available?available/item.Width:1,w=item.Width*scale,ih=item.Height*scale;
   float gap=x==padding?0:Math.Max(0,item.GapBefore);
   if(x>padding&&x+gap+w>width-padding+.01f){row++;y+=h+rowGap;x=padding;h=rowHeight;gap=0;}
   result.Add(new(i,x+gap,y,w,ih,scale,row));x+=gap+w;h=Math.Max(h,ih);
  }
  foreach(var group in result.GroupBy(p=>p.Row).ToArray()){
   var entries=group.ToArray();float height=Math.Max(rowHeight,entries.Max(p=>p.Height));
   int firstRight=entries.Length;while(firstRight>0&&items[entries[firstRight-1].Index].Right)firstRight--;
   float shift=width-padding-(entries[^1].X+entries[^1].Width);
   for(int j=0;j<entries.Length;j++){var p=entries[j];result[p.Index]=p with{X=p.X+(j>=firstRight?shift:0),Y=p.Y+(height-p.Height)/2};}
  }
  return new(result.ToArray(),items.Count==0?1:row+1,items.Count==0?rowHeight:y+h);
 }
}
