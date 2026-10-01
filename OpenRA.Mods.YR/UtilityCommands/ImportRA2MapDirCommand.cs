using OpenRA.Mods.Cnc.UtilityCommands;
using System;
using System.Collections.Generic;
using System.IO;
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
namespace OpenRA.Mods.YR.UtilityCommands
{
	class ImportRA2MapDirCommand : IUtilityCommand
	{
		public string Name { get { return "--import-ra2-map-dir"; } }
		bool IUtilityCommand.ValidateArguments(string[] args) { return args.Length >= 2; }
		[Desc("DIRECTORY", "Convert Red Alert 2 maps from specific directory.")]
		public void Run(Utility utility, string[] args)
		{
			if (Directory.Exists(args[1]))
			{
				DirectoryInfo di = new DirectoryInfo(args[1]);
				//Find all *.map files and run import-ra-map command
				var maps = di.EnumerateFiles("*.map");
				foreach (var map in maps)
				{
					//ImportRA2MapCommand importRA2MapCommand = new ImportRA2MapCommand();
					//importRA2MapCommand.Run(utility, new string[] { string.Empty, map.FullName});
				}
			}
		}
	}
}