using MyMod;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace WastelandLizard
{
    // ========== 数字输入对话框 ==========
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
            Widgets.Label(new Rect(0, 0, inRect.width, 30), "Set Channel (0-99)");

            Text.Font = GameFont.Small;
            GUI.SetNextControlName("ChannelField");

            editBuffer = GUI.TextField(new Rect(0, 40, inRect.width, 30), editBuffer);

            if (GUI.Button(new Rect(0, 80, inRect.width / 2 - 5, 30), "Confirm"))
            {
                TrySetChannel();
            }

            if (GUI.Button(new Rect(inRect.width / 2 + 5, 80, inRect.width / 2 - 5, 30), "Cancel"))
            {
                Close();
            }
        }

        void TrySetChannel()
        {
            int value;
            if (int.TryParse(editBuffer, out value))
            {
                value = Mathf.Clamp(value, 0, 99);
                teleporter.SetChannel(value);
                Close();
            }
            else
            {
                Messages.Message("Invalid number", MessageTypeDefOf.RejectInput);
            }
        }

        public override void OnAcceptKeyPressed()
        {
            TrySetChannel();
        }

        public override Vector2 InitialSize => new Vector2(300, 150);
    }
}
