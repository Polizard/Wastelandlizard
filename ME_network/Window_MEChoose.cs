using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WastelandLizard
{
    /// <summary>
    /// 选择ME网络中物品的窗口
    /// 支持选择模式（有回调）和纯查看模式（无回调时点击无效）
    /// </summary>
    public class Window_MEChoose : Window
    {
        private Map map;
        private Action<ThingDef> onSelected;       // 选择回调，为null时表示仅查看
        private List<ThingDef> availableDefs;       // 当前页显示的物品Def
        private Dictionary<ThingDef, int> allContents; // 所有网络内容
        private int currentPage = 0;
        private const int itemsPerPage = 18;        // 3列6行
        private const int colCount = 3;
        private const int rowCount = 6;
        private bool selectable;                     // 是否可点击选择

        public override Vector2 InitialSize => new Vector2(500f, 500f);

        /// <param name="map">当前地图</param>
        /// <param name="onSelected">选择回调，为null时窗口为只读模式</param>
        public Window_MEChoose(Map map, Action<ThingDef> onSelected = null)
        {
            this.map = map;
            this.onSelected = onSelected;
            this.selectable = onSelected != null;
            this.doCloseButton = true;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;

            // 获取网络内容
            var network = map.GetComponent<MapComponent_MEStorage>();
            if (network != null)
            {
                allContents = network.GetAllContents();
                // 按物品名称排序
                var sorted = allContents.Keys.OrderBy(def => def.label).ToList();
                availableDefs = sorted;
            }
            else
            {
                allContents = new Dictionary<ThingDef, int>();
                availableDefs = new List<ThingDef>();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (availableDefs.Count == 0)
            {
                Widgets.Label(inRect, "网络中没有任何物品。");
                return;
            }

            // 计算分页
            int totalPages = Mathf.CeilToInt((float)availableDefs.Count / itemsPerPage);
            currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);

            // 绘制物品网格
            float cellWidth = (inRect.width - 20f) / colCount;
            float cellHeight = 40f; // 每行高度
            float startX = inRect.x;
            float startY = inRect.y + 30f; // 留出标题空间

            int startIdx = currentPage * itemsPerPage;
            int endIdx = Mathf.Min(startIdx + itemsPerPage, availableDefs.Count);

            for (int i = startIdx; i < endIdx; i++)
            {
                int idx = i - startIdx;
                int col = idx % colCount;
                int row = idx / colCount;
                Rect cellRect = new Rect(startX + col * cellWidth, startY + row * cellHeight, cellWidth, cellHeight);

                ThingDef def = availableDefs[i];
                int count = allContents[def];

                // 鼠标悬停高亮
                if (Mouse.IsOver(cellRect))
                {
                    Widgets.DrawHighlight(cellRect);
                }

                // 绘制图标
                Widgets.ThingIcon(new Rect(cellRect.x, cellRect.y, 30f, 30f), def);
                // 绘制名称和数量
                Widgets.Label(new Rect(cellRect.x + 35f, cellRect.y, cellRect.width - 35f, cellRect.height),
                    string.Format("{0} ({1})", def.LabelCap, count));

                // 如果是选择模式，添加不可见按钮；查看模式不响应点击
                if (selectable)
                {
                    if (Widgets.ButtonInvisible(cellRect))
                    {
                        onSelected?.Invoke(def);
                        Find.WindowStack.TryRemove(this);
                    }
                }
                // 查看模式下，仅显示，不添加按钮
            }

            // 翻页按钮
            float pageY = inRect.yMax - 40f;
            if (currentPage > 0)
            {
                if (Widgets.ButtonText(new Rect(inRect.x, pageY, 80f, 30f), "< 上一页"))
                {
                    currentPage--;
                }
            }
            if (currentPage < totalPages - 1)
            {
                if (Widgets.ButtonText(new Rect(inRect.xMax - 80f, pageY, 80f, 30f), "下一页 >"))
                {
                    currentPage++;
                }
            }

            // 显示页码（使用固定宽度避免 CalcSize 潜在问题）
            string pageLabel = string.Format("第 {0}/{1} 页", currentPage + 1, totalPages);
            float labelWidth = 100f; // 固定宽度
            Widgets.Label(new Rect(inRect.center.x - labelWidth / 2f, pageY, labelWidth, 30f), pageLabel);
        }
    }
}