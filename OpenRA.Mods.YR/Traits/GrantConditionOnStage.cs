#region Copyright & License Information
/*
 * Written by Cook Green of YR Mod
 * Follows GPLv3 License as the OpenRA engine:
 * 
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Graphics;
namespace OpenRA.Mods.YR.Traits
{
    public class GrantConditionOnStageInfo : ConditionalTraitInfo
    {
        /// <summary>
        /// condition-delay dictionary
        /// </summary>
        public Dictionary<string, int> Conditions;
        public override object Create(ActorInitializer init)
        {
            return new GrantConditionOnStage(init, this);
        }
    }
    public class GrantConditionOnStage : ConditionalTrait<GrantConditionOnStageInfo>, ITick
    {
        private string currentCondition;
        private Dictionary<string, int> conditions;
        private int delay = -1;
        private int currentConditionToken = Actor.InvalidConditionToken;
        private int currentConditionIndex;
        public GrantConditionOnStage(ActorInitializer init, GrantConditionOnStageInfo info) : base(info)
        {
            this.conditions = info.Conditions;
            currentCondition = conditions.ElementAt(0).Key;
            delay = conditions.ElementAt(0).Value;
            currentConditionIndex = 0;
        }
        protected override void Created(Actor self)
        {
            
            currentConditionToken = self.GrantCondition(currentCondition);
        }
        public void Tick(Actor self)
        {
            if (delay >= 0)
            {
                if (delay == 0)
                {
                    if (currentConditionIndex == conditions.Count - 1)
                    {
                        currentConditionIndex = 0;
                    }
                    else
                    {
                        currentConditionIndex++;
                    }
                    currentConditionToken = self.RevokeCondition(currentConditionToken);
                    currentConditionToken = self.GrantCondition(conditions.ElementAt(currentConditionIndex).Key);
                    delay = conditions.ElementAt(currentConditionIndex).Value;
                }
                else
                {
                    delay--;
                }
            }
        }
    }
}