using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace WastelandLizard
{
    public class Dialog_SetChannel : Window
    {
        private Building_Teleporter teleporter;
        private string editBuffer;

        public Dialog_SetChannel(Building_Teleporter tp)
        {
            teleporter = tp;
            editBuffer = tp.Channel.ToString();
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 30), "设置频道 (0 或正整数)");

            Text.Font = GameFont.Small;
            GUI.SetNextControlName("ChannelField");
            editBuffer = Widgets.TextField(new Rect(0, 40, inRect.width, 30), editBuffer);

            if (Widgets.ButtonText(new Rect(0, 80, inRect.width / 2 - 5, 30), "确认"))
            {
                TrySetChannel();
            }

            if (Widgets.ButtonText(new Rect(inRect.width / 2 + 5, 80, inRect.width / 2 - 5, 30), "取消"))
            {
                Close();
            }
        }

        // 修改：取消上限，只检查是否为非负整数
        private void TrySetChannel()
        {
            if (int.TryParse(editBuffer, out int value))
            {
                if (value >= 0)
                {
                    teleporter.SetChannel(value);
                    Close();
                }
                else
                {
                    Messages.Message("频道不能为负数", MessageTypeDefOf.RejectInput);
                }
            }
            else
            {
                Messages.Message("无效数字", MessageTypeDefOf.RejectInput);
            }
        }

        public override void OnAcceptKeyPressed()
        {
            TrySetChannel();
        }

        public override Vector2 InitialSize => new Vector2(300, 150);
    }
}