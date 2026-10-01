#region Copyright & License Information
/*
 * Written by Cook Green of YR Mod
 * Follows GPLv3 License as the OpenRA engine:
 *
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion
using OpenRA.Activities;
using OpenRA.Mods.YR.Traits;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Graphics;
namespace OpenRA.Mods.YR.Activities
{
    class MarkerSlaveLeave : Activity
    {
		private Actor slave;
        private Actor master;
        public MarkerSlaveLeave(Actor slave, Actor master)
        {
            this.master = master;
			this.slave = slave;
        }
        public override bool Tick(Actor self)
        {
            if (self.IsDead)
                return false;
            self.World.AddFrameEndTask(w =>
            {
                if (!master.IsDead && master.IsInWorld)
                {
                    master.Trait<MarkerMaster>().PickupSlave(master, slave);
                }
                self.World.Remove(self);
            });
            return false;
        }
    }
}