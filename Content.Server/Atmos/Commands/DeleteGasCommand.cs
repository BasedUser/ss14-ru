using Content.Server.Administration;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Administration;
using Content.Shared.Atmos;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server.Atmos.Commands
{
    [AdminCommand(AdminFlags.Debug)]
    public sealed class DeleteGasCommand : IConsoleCommand
    {
        public string Command => "deletegas";
        public string Description => "Removes all gases from a grid, or just of one type if specified.";
        public string Help => $"Usage: {Command} <GridId> <Gas> / {Command} <GridId> / {Command} <Gas> / {Command}";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var player = shell.Player as IPlayerSession;
            EntityUid gridId;
            Gas? gas = null;

            var entMan = IoCManager.Resolve<IEntityManager>();

            switch (args.Length)
            {
                case 0:
                {
                    if (player == null)
                    {
                        shell.WriteLine("A grid must be specified when the command isn't used by a player.");
                        return;
                    }

                    if (player.AttachedEntity is not {Valid: true} playerEntity)
                    {
                        shell.WriteLine("You have no entity to get a grid from.");
                        return;
                    }

                    gridId = entMan.GetComponent<TransformComponent>(playerEntity).GridEntityId;

                    if (gridId == EntityUid.Invalid)
                    {
                        shell.WriteLine("You aren't on a grid to delete gas from.");
                        return;
                    }

                    break;
                }
                case 1:
                {
                    if (!EntityUid.TryParse(args[0], out var number))
                    {
                        // Argument is a gas
                        if (player == null)
                        {
                            shell.WriteLine("A grid id must be specified if not using this command as a player.");
                            return;
                        }

                        if (player.AttachedEntity is not {Valid: true} playerEntity)
                        {
                            shell.WriteLine("You have no entity from which to get a grid id.");
                            return;
                        }

                        gridId = entMan.GetComponent<TransformComponent>(playerEntity).GridEntityId;

                        if (gridId == EntityUid.Invalid)
                        {
                            shell.WriteLine("You aren't on a grid to delete gas from.");
                            return;
                        }

                        if (!Enum.TryParse<Gas>(args[0], true, out var parsedGas))
                        {
                            shell.WriteLine($"{args[0]} is not a valid gas name.");
                            return;
                        }

                        gas = parsedGas;
                        break;
                    }

                    // Argument is a grid
                    gridId = number;
                    break;
                }
                case 2:
                {
                    if (!EntityUid.TryParse(args[0], out var first))
                    {
                        shell.WriteLine($"{args[0]} is not a valid integer for a grid id.");
                        return;
                    }

                    gridId = first;

                    if (gridId == EntityUid.Invalid)
                    {
                        shell.WriteLine($"{gridId} is not a valid grid id.");
                        return;
                    }

                    if (!Enum.TryParse<Gas>(args[1], true, out var parsedGas))
                    {
                        shell.WriteLine($"{args[1]} is not a valid gas.");
                        return;
                    }

                    gas = parsedGas;

                    break;
                }
                default:
                    shell.WriteLine(Help);
                    return;
            }

            var mapManager = IoCManager.Resolve<IMapManager>();

            if (!mapManager.TryGetGrid(gridId, out _))
            {
                shell.WriteLine($"No grid exists with id {gridId}");
                return;
            }

            var atmosphereSystem = EntitySystem.Get<AtmosphereSystem>();

            var tiles = 0;
            var moles = 0f;

            if (gas == null)
            {
                foreach (var tile in atmosphereSystem.GetAllTileMixtures(gridId, true))
                {
                    if (tile.Immutable) continue;

                    tiles++;
                    moles += tile.TotalMoles;

                    tile.Clear();
                }
            }
            else
            {
                foreach (var tile in atmosphereSystem.GetAllTileMixtures(gridId, true))
                {
                    if (tile.Immutable) continue;

                    tiles++;
                    moles += tile.TotalMoles;

                    tile.SetMoles(gas.Value, 0);
                }
            }

            if (gas == null)
            {
                shell.WriteLine($"Removed {moles} moles from {tiles} tiles.");
                return;
            }

            shell.WriteLine($"Removed {moles} moles of gas {gas} from {tiles} tiles.");
        }
    }

}
