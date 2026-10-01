using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Graphics;
namespace OpenRA.Mods.YR.WDT
{
	public class WDTData
	{
		public List<WDTScenario> Scenarios;
		public Dictionary<string, List<WDTBlock>> Blocks;
		public WDTData()
		{
			Scenarios = new List<WDTScenario>();
			Blocks = new Dictionary<string, List<WDTBlock>>();
		}
	}
}