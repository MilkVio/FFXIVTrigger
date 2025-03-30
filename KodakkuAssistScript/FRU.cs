using System;
using System.Numerics;
using KodakkuAssist.Module.GameEvent;
using KodakkuAssist.Script;
using KodakkuAssist.Module.GameEvent.Struct;
using KodakkuAssist.Module.Draw;
using System.Windows.Forms;
using System.Threading;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

namespace MyScriptNamespace
{
    [ScriptType(name: "绝伊甸P5地火格子指路", territorys: [1238], guid: "A20DB976-B60D-E62D-B93E-A164275C13AD", version: "0.0.0.9", author: "KnightRider")]
    public class FRUScript
    {
        [UserSetting("地火安全指路颜色")]
        public ScriptColor BladeSafeColor { get; set; } = new() { V4 = new(1, 1, 1, 1) };
        [UserSetting("地火危险指路颜色")]
        public ScriptColor BladeDangerColor { get; set; } = new() { V4 = new(1, 0, 0, 1) };
        
        private string Phase = "";
        private Vector2? Point1 = new Vector2(0f, 0f);
        private Vector2? Point2 = new Vector2(0f, 0f);
        private Vector2? Point3 = new Vector2(0f, 0f);
        private Vector2? MiddlePoint = new Vector2(0f, 0f);
        private onPoint? OnPoint = null;
        private int bladeCount = 0;
        
        public class Blade
        {
            public UInt32 Id { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Rotation { get; set; }
            public Blade(UInt32 id, double x, double y, double rotation)
            {
                Id = id;
                X = x;
                Y = y;
                Rotation = rotation;
            }
        }
        public class onPoint
        {
            public string Name { get; set; }
            public Vector2 OnCoord { get; set; }//      Point
            public Vector2 Coord1 { get; set; }//         1
            public Vector2 Coord2 { get; set; }//     /       \
            
                                               //   4|         |2
            public Vector2 Coord3 { get; set; }//     \       /
            public Vector2 Coord4 { get; set; }//         3
            
            public onPoint(string name, Vector2 onCoord, Vector2 coord1, Vector2 coord2, Vector2 coord3, Vector2 coord4)
            {
                Name = name;
                this.OnCoord = onCoord;
                this.Coord1 = coord1;
                this.Coord2 = coord2;
                this.Coord3 = coord3;
                this.Coord4 = coord4;
            }
        }
        //private List<Blade> blades = new List<Blade>();
        private ConcurrentBag<Blade> blades = new ConcurrentBag<Blade>();
        private List<Blade> P1P3Blades = new List<Blade>();
        private List<onPoint> onPoints = new List<onPoint>();
        private List<Vector2?> BladeRoutes;
        
        public void Init(ScriptAccessory accessory)
        {
            blades.Clear();
            P1P3Blades.Clear();
            onPoints.Clear();
            BladeRoutes = Enumerable.Repeat<Vector2?>(null, 7).ToList();
            onPoints.Add(new onPoint("A", new Vector2(100, 93), new Vector2(100, 91.5f), new Vector2(101.4f, 92.9f), new Vector2(100, 94.3f), new Vector2(98.6f, 92.9f)));
            onPoints.Add(new onPoint("B", new Vector2(107, 100), new Vector2(108.5f, 100), new Vector2(107, 101.4f), new Vector2(105.6f, 100), new Vector2(107, 98.6f)));
            onPoints.Add(new onPoint("C", new Vector2(100, 107), new Vector2(100, 108.5f), new Vector2(98.6f, 107), new Vector2(100, 105.6f), new Vector2(101.4f, 107.1f)));
            onPoints.Add(new onPoint("D", new Vector2(93, 100), new Vector2(91.5f, 100), new Vector2(93, 98.6f), new Vector2(94.4f, 100), new Vector2(93, 101.4f)));
        }

        public static Vector2? mathPoint(Blade b1, Blade b2)
        {
            //计算方向的正弦和余弦
            float s1 = (float)Math.Sin(b1.Rotation);
            float c1 = (float)Math.Cos(b1.Rotation);
            float s2 = (float)Math.Sin(b2.Rotation);
            float c2 = (float)Math.Cos(b2.Rotation);
    
            //起点
            float x1 = (float)b1.X;
            float y1 = (float)b1.Y;
            float x2 = (float)b2.X;
            float y2 = (float)b2.Y;

            //计算分母
            float d = s1 * c2 - s2 * c1;

            //检查合法
            if (Math.Abs(d) < 1e-10)
            {
                return null; // 平行
            }

            //计算交点 感恩阿洛
            float X = (x1 * s1 * c2 - x2 * s2 * c1 - (y2 - y1) * c1 * c2) / d;
            float Y = (y2 * c2 * s1 - y1 * c1 * s2 + (x2 - x1) * s1 * s2) / d;

            return new Vector2(X, Y);
        }

        public static Vector2? middlePoint(Vector2? P1, Vector2? P2)
        {
            if (P1.HasValue && P2.HasValue)
            {
                float midX = (P1.Value.X + P2.Value.X) / 2;
                float midY = (P1.Value.Y + P2.Value.Y) / 2;
                return new Vector2(midX, midY);
            }
            return null; //如果任意一个点为 null，返回 null
        }
        
        //获取与中点最近的点
        public static onPoint FindClosestOnPoint(List<onPoint> points, Vector2? target)
        {
            onPoint closestPoint = null;
            float closestDistance = float.MaxValue;
            foreach (var point in points)
            {
                // 计算距离
                float distance = Vector2.Distance(point.OnCoord, target.Value);
                //如果当前距离小于已知最小距离，则更新
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPoint = point;
                }
            }
            return closestPoint;//返回最接近的点
        }
        
        //正点子点最远
        public static Vector2 FindFarthestPoint(onPoint point, Vector2? referencePoint)
        {
            //存储所有坐标
            Vector2[] coords = { point.Coord1, point.Coord2, point.Coord3, point.Coord4 };
            float maxDistance = float.MinValue;//初始化最大距离
            Vector2 farthestCoord = Vector2.Zero;//初始化最远坐标
            //遍历所有坐标找最远
            foreach (var coord in coords)
            {
                float distance = Vector2.Distance(coord, referencePoint.Value);//计算距离
                if (distance > maxDistance)//如果当前距离大于已知最大距离
                {
                    maxDistance = distance;//更新最大距离
                    farthestCoord = coord;//更新最远坐标
                }
            }
            return farthestCoord;//返回最远的坐标
        }
        
        //正点子点最近
        public static Vector2 FindClosestPoint(onPoint point, Vector2? referencePoint)
        {
            //存储所有坐标
            Vector2[] coords = { point.Coord1, point.Coord2, point.Coord3, point.Coord4 };
    
            float minDistance = float.MaxValue;//初始化最小距离
            Vector2 closestCoord = Vector2.Zero;//初始化最近坐标

            // 遍历所有坐标，找到最近的
            foreach (var coord in coords)
            {
                float distance = Vector2.Distance(coord, referencePoint.Value);//计算距离
                if (distance < minDistance)//如果当前距离小于已知最小距离
                {
                    minDistance = distance;//更新最小距离
                    closestCoord = coord;//更新最近坐标
                }
            }
            return closestCoord;//返回最近的坐标
        }

        public static Vector3 Vector3Fucker(Vector2? V)
        {
            Vector3 result = new Vector3();
            if (V.HasValue)
            {
                result.X = V.Value.X;
                result.Y = 0;
                result.Z = V.Value.Y;
            }
            return result;
        }
        
        [ScriptMethod(name: "P5地火", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:40306"],userControl:false)]
        public void 阶段记录_P5地火(Event @event, ScriptAccessory accessory)
        {
            Phase = "P5地火";
            blades.Clear();
            P1P3Blades.Clear();
            BladeRoutes.Clear();
            bladeCount = 0;
            BladeRoutes = Enumerable.Repeat<Vector2?>(null, 7).ToList();
        }
        
        [ScriptMethod(name: "调试开关", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:40306"],userControl:true)]
        public void 阶段记录_P5调试(Event @event, ScriptAccessory accessory)
        {
            accessory.Method.SendChat($"/e KnightRider祝地火順利~");
        }
        
        private readonly object bladeLock = new object();
        //捕获组
        [ScriptMethod(name: "地火数据捕获", eventType: EventTypeEnum.ObjectEffect, eventCondition: ["Id1:1"], userControl:false)]
        public void 地火数据捕获(Event @event, ScriptAccessory accessory)
        {
            if (Phase == "P5地火")//捕获限定区域
            {
                lock (bladeLock)
                {
                    if (bladeCount < 7) //如果地火<7就继续捕获
                    {
                        var pos = JsonConvert.DeserializeObject<Vector3>(@event["SourcePosition"]);
                        //存入数据
                        blades.Add(new Blade(
                            id: Convert.ToUInt32(@event["SourceId"], 16),
                            x: Convert.ToDouble(pos.X),
                            y: Convert.ToDouble(pos.Z),
                            rotation: Convert.ToDouble(@event["SourceRotation"])
                        ));
                        bladeCount++;
                        //accessory.Method.SendChat($"/e {bladeCount}");
                    }
                    if (blades.Count == 6) //如果收集到了6个地火数据
                    {
                        ProcessBlades();//处理三个交点+更改阶段=>P5地火计算完成
                    }
                }
            }
        }

        private void ProcessBlades()
        {
            //accessory.Method.SendChat($"/e 收集完成");
            var sortedBlades = blades.OrderBy(b => b.Id).ToList();//按OID排序List
            //accessory.Method.SendChat($"/e 排序完成");
            if (sortedBlades != null)
            {
                //accessory.Method.SendChat($"/e 排序完成");
                //存入13点
                P1P3Blades.Add(sortedBlades[0]);
                P1P3Blades.Add(sortedBlades[1]);
                P1P3Blades.Add(sortedBlades[4]);
                P1P3Blades.Add(sortedBlades[5]);
                //计算三个交点
                Point1 = mathPoint(sortedBlades[0], sortedBlades[1]);//计算第1交点
                Point2 = mathPoint(sortedBlades[2], sortedBlades[3]);//计算第2交点
                Point3 = mathPoint(sortedBlades[4], sortedBlades[5]);//计算第3交点
                MiddlePoint = middlePoint(Point1, Point3);//计算第13中点
                OnPoint = FindClosestOnPoint(onPoints,MiddlePoint);//计算从哪个正点开始起跑
                //accessory.Method.SendChat($"/e 去{OnPoint.Name}点起跑");
                Phase = "P5地火计算完成";
            }
        }
        
        private readonly object drawLock = new object();
         //1火开始发光特效
        [ScriptMethod(name: "地火数据捕获2", eventType: EventTypeEnum.ObjectEffect, eventCondition: ["Id2:16"], userControl:false)]
        public void P5_地火2(Event @event, ScriptAccessory accessory)
        {
             if (Phase == "P5地火计算完成")//限制分组
             {
                 lock (drawLock)
                 {
                     Phase = "P5运算结束";
                     //accessory.Method.SendChat($"/e P5运算结束");
                     var id = Convert.ToUInt32(@event["SourceId"], 16);
                     Vector2 FarthestPoint = new Vector2();
                     Vector2 ClosestPoint = new Vector2();
                     if (id == P1P3Blades[0].Id || id == P1P3Blades[1].Id) //P1起火
                     {
                         FarthestPoint = FindFarthestPoint(OnPoint, Point1);
                         ClosestPoint = FindClosestPoint(OnPoint, Point1);
                     }
                     else if (id == P1P3Blades[2].Id || id == P1P3Blades[3].Id) //P3起火
                     {
                         FarthestPoint = FindFarthestPoint(OnPoint, Point3);
                         ClosestPoint = FindClosestPoint(OnPoint, Point3);
                     }

                     //远 近 近 远
                     BladeRoutes.Insert(0, FarthestPoint); //第1跑起点 与起火点相对最远
                     BladeRoutes.Insert(1, ClosestPoint); //第2跑起点 与起火点相对最近
                     BladeRoutes.Insert(2, FindFarthestPoint(OnPoint, Point2)); //第3跑路径是上还是下 相对P2最远
                     BladeRoutes.Insert(3, FindClosestPoint(OnPoint, Point2)); //第4跑路径是上还是下 相对P2最近
                     BladeRoutes.Insert(4, ClosestPoint); //第5跑起点 与起火点相对最近
                     BladeRoutes.Insert(5, FarthestPoint); //第5跑起点 与起火点相对最远

                     //指路初期想法： 0绿1红 1绿出2红 2绿出3红
                     //每次2000毫秒？
                     int BladeTimes = 2000;
                     //0绿1红
                     var Goline0 = accessory.Data.GetDefaultDrawProperties();
                     Goline0.Owner = accessory.Data.Me;
                     Goline0.DestoryAt = 9000;
                     Goline0.Color = BladeSafeColor.V4;
                     Goline0.Scale = new(1);
                     Goline0.ScaleMode |= ScaleMode.YByDistance;
                     Goline0.TargetPosition = Vector3Fucker(BladeRoutes[0]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, Goline0);

                     var line1 = accessory.Data.GetDefaultDrawProperties();
                     line1.Position = Vector3Fucker(BladeRoutes[0]);
                     line1.DestoryAt = 9000;
                     line1.Color = BladeDangerColor.V4;
                     line1.Scale = new(1);
                     line1.ScaleMode |= ScaleMode.YByDistance;
                     line1.TargetPosition = Vector3Fucker(BladeRoutes[1]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, line1);
                     /////////////////////////////////////1绿放2 基础延迟9000
                     var Goline1 = accessory.Data.GetDefaultDrawProperties();
                     Goline1.Owner = accessory.Data.Me;
                     Goline1.Delay = 9000;
                     Goline1.DestoryAt = BladeTimes;
                     Goline1.Color = BladeSafeColor.V4;
                     Goline1.Scale = new(1);
                     Goline1.ScaleMode |= ScaleMode.YByDistance;
                     Goline1.TargetPosition = Vector3Fucker(BladeRoutes[1]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, Goline1);

                     var line2 = accessory.Data.GetDefaultDrawProperties();
                     line2.Position = Vector3Fucker(BladeRoutes[1]);
                     line2.Delay = 9000;
                     line2.DestoryAt = BladeTimes;
                     line2.Color = BladeDangerColor.V4;
                     line2.Scale = new(1);
                     line2.ScaleMode |= ScaleMode.YByDistance;
                     line2.TargetPosition = Vector3Fucker(BladeRoutes[2]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, line2);

                     /////////////////////////////////////2绿放3 基础延迟9000+bladetime
                     var Goline2 = accessory.Data.GetDefaultDrawProperties();
                     Goline2.Owner = accessory.Data.Me;
                     Goline2.Delay = 9000 + BladeTimes;
                     Goline2.DestoryAt = BladeTimes;
                     Goline2.Color = BladeSafeColor.V4;
                     Goline2.Scale = new(1);
                     Goline2.ScaleMode |= ScaleMode.YByDistance;
                     Goline2.TargetPosition = Vector3Fucker(BladeRoutes[2]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, Goline2);

                     var line3 = accessory.Data.GetDefaultDrawProperties();
                     line3.Position = Vector3Fucker(BladeRoutes[2]);
                     line3.Delay = 9000 + BladeTimes;
                     line3.DestoryAt = BladeTimes;
                     line3.Color = BladeDangerColor.V4;
                     line3.Scale = new(1);
                     line3.ScaleMode |= ScaleMode.YByDistance;
                     line3.TargetPosition = Vector3Fucker(BladeRoutes[3]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, line3);

                     /////////////////////////////////////3绿放4 基础延迟9000+bladetime*2
                     var Goline3 = accessory.Data.GetDefaultDrawProperties();
                     Goline3.Owner = accessory.Data.Me;
                     Goline3.Delay = 9000 + BladeTimes * 2;
                     Goline3.DestoryAt = BladeTimes;
                     Goline3.Color = BladeSafeColor.V4;
                     Goline3.Scale = new(1);
                     Goline3.ScaleMode |= ScaleMode.YByDistance;
                     Goline3.TargetPosition = Vector3Fucker(BladeRoutes[3]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, Goline3);

                     var line4 = accessory.Data.GetDefaultDrawProperties();
                     line4.Position = Vector3Fucker(BladeRoutes[3]);
                     line4.Delay = 9000 + BladeTimes * 2;
                     line4.DestoryAt = BladeTimes;
                     line4.Color = BladeDangerColor.V4;
                     line4.Scale = new(1);
                     line4.ScaleMode |= ScaleMode.YByDistance;
                     line4.TargetPosition = Vector3Fucker(BladeRoutes[4]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, line4);

                     /////////////////////////////////////4绿放5 基础延迟9000+bladetime*3
                     var Goline4 = accessory.Data.GetDefaultDrawProperties();
                     Goline4.Owner = accessory.Data.Me;
                     Goline4.Delay = 9000 + BladeTimes * 3;
                     Goline4.DestoryAt = BladeTimes;
                     Goline4.Color = BladeSafeColor.V4;
                     Goline4.Scale = new(1);
                     Goline4.ScaleMode |= ScaleMode.YByDistance;
                     Goline4.TargetPosition = Vector3Fucker(BladeRoutes[4]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, Goline4);

                     var line5 = accessory.Data.GetDefaultDrawProperties();
                     line5.Position = Vector3Fucker(BladeRoutes[4]);
                     line5.Delay = 9000 + BladeTimes * 3;
                     line5.DestoryAt = BladeTimes;
                     line5.Color = BladeDangerColor.V4;
                     line5.Scale = new(1);
                     line5.ScaleMode |= ScaleMode.YByDistance;
                     line5.TargetPosition = Vector3Fucker(BladeRoutes[5]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, line5);

                     /////////////////////////////////////5绿放6 基础延迟9000+bladetime*4
                     var Goline5 = accessory.Data.GetDefaultDrawProperties();
                     Goline5.Owner = accessory.Data.Me;
                     Goline5.Delay = 9000 + BladeTimes * 4;
                     Goline5.DestoryAt = BladeTimes;
                     Goline5.Color = BladeSafeColor.V4;
                     Goline5.Scale = new(1);
                     Goline5.ScaleMode |= ScaleMode.YByDistance;
                     Goline5.TargetPosition = Vector3Fucker(BladeRoutes[5]);
                     accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, Goline5);
                 }
             }
        }
    }
}
