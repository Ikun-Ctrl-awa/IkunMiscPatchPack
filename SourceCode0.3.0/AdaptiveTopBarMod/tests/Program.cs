using AdaptiveTopBar;
int pass=0;void Check(string name,bool condition){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);pass++;}
var native=new[]{new FlowItem(88,80),new FlowItem(179,80,24),new FlowItem(150,80,24),new FlowItem(240,80,24),new FlowItem(200,80,24),new FlowItem(164,80,0,true),new FlowItem(80,80,0,true),new FlowItem(80,80,0,true),new FlowItem(80,80,0,true)};
var wide=FlowLayout.Pack(1920,native);Check("Native bar remains one row",wide.Rows==1);Check("Right controls remain aligned to edge",Math.Abs(wide.Items[^1].X+wide.Items[^1].Width-1904)<.01);
var crowded=native.Concat(Enumerable.Repeat(new FlowItem(80,80,4,true),45)).ToArray();var multi=FlowLayout.Pack(1280,crowded);Check("Overflow creates three or more rows",multi.Rows>=3);Check("No fixed three-row limit",FlowLayout.Pack(640,crowded).Rows>3);
Check("Wider window collapses rows",FlowLayout.Pack(3000,crowded).Rows<multi.Rows);Check("Removing icons returns to one row",FlowLayout.Pack(1280,new[]{new FlowItem(80,80)}).Rows==1);
var oversized=FlowLayout.Pack(640,new[]{new FlowItem(1500,80)});Check("Indivisible wide item stays inside available width",oversized.Items[0].Scale<1&&oversized.Items[0].Width<=608);
Check("Empty bar is a valid single row",FlowLayout.Pack(1280,Array.Empty<FlowItem>()).Rows==1);
Check("Variable height contributes to total height",FlowLayout.Pack(320,new[]{new FlowItem(200,150),new FlowItem(200,80)}).Height>=234);
var rng=new Random(20261019);for(int trial=0;trial<3000;trial++){
 float width=rng.Next(160,3841);var items=Enumerable.Range(0,rng.Next(1,140)).Select(i=>new FlowItem(rng.Next(20,2001),rng.Next(30,151),rng.Next(0,25),false)).ToArray();var plan=FlowLayout.Pack(width,items);
 if(plan.Items.Length!=items.Length)throw new Exception("Item lost");
 foreach(var p in plan.Items)if(p.X<15.99||p.X+p.Width>width-15.99||p.Y<0||p.Y+p.Height>plan.Height+.02)throw new Exception("Out of bounds");
 foreach(var group in plan.Items.GroupBy(p=>p.Row)){var row=group.ToArray();for(int i=1;i<row.Length;i++)if(row[i-1].X+row[i-1].Width>row[i].X+.02)throw new Exception("Overlap");}
}
Check("3000 random layouts retain all items without overlap or overflow",true);
Console.WriteLine($"TEST_RESULT passed={pass} random_cases=3000");
