using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

using UnityModManagerNet;

namespace PersistentJobsMod
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Allow shunting jobs to be (cheatily) accepted even on loading tracks")]
        public bool AllowAccOnWarehouseTracks = false;

        public bool ShuntJobInteractFlag { get; set; } = false;

        public void SetShuntJobInteract(bool Bool) => ShuntJobInteractFlag = Bool;
        public bool GetShuntJobInteract() => ShuntJobInteractFlag;

        public override void Save(UnityModManager.ModEntry entry)
        {
            Save(this, entry);
        }

        public void OnChange() { }
    }
}
