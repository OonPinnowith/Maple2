﻿using System.Diagnostics;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.PacketHandlers;

public class LoadUgcMapHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.RequestLoadUgcMap;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GameStorage GameStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        Debug.Assert(packet.ReadInt() == GameSession.FIELD_KEY);
        if (session.Field == null) {
            return;
        }

        List<PlotCube> plotCubes = [];
        foreach (Plot plot in session.Field.Plots.Values) {
            foreach (PlotCube cube in plot.Cubes.Values) {
                cube.PlotId = plot.Number;
                plotCubes.Add(cube);
            }
        }

        if (session.Field.MapId is not Constant.DefaultHomeMapId || session.Field.OwnerId <= 0) {
            session.Send(LoadUgcMapPacket.Load(plotCubes.Count));

            LoadPlots(session, plotCubes);
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        Home? home = db.GetHome(session.Field.OwnerId);
        if (home == null) {
            return;
        }

        // Check if current plot is planner
        Plot indoor = session.Field.Plots.First().Value;
        if (indoor.IsPlanner) {
            home.EnterPlanner(indoor.PlotMode);
        }

        List<PlotCube> entryPortals = plotCubes.Where(x => x.ItemId is Constant.PortalEntryId).ToList();
        if (entryPortals.Count > 0) {
            PlotCube entryPortal = entryPortals.OrderBy(_ => Random.Shared.Next()).First();
            session.Player.Position = entryPortal.Position;
            session.Player.Rotation = new Vector3(0, 0, entryPortal.Rotation);
            session.Player.Rotation -= new Vector3(0, 0, 180);
        } else {
            session.Player.Position = home.CalculateSafePosition(plotCubes);
        }

        // Technically this sends home details to all players who enter map (including passcode)
        // but you would already know passcode if you entered the map.
        session.Send(LoadUgcMapPacket.LoadHome(plotCubes.Count, home));

        LoadPlots(session, plotCubes);

        // this is a workaround for the client to load the map before field add player - without this, player will fall and get tp'd back to 0,0
        Task.Delay(200).Wait();
    }

    private static void LoadPlots(GameSession session, List<PlotCube> plotCubes) {
        lock (session.Field.Plots) {
            session.Send(LoadCubesPacket.PlotOwners(session.Field.Plots.Values));
            session.Send(LoadCubesPacket.Load(plotCubes));
            session.Send(LoadCubesPacket.PlotState([.. session.Field.Plots.Values]));
            session.Send(LoadCubesPacket.PlotExpiry([.. session.Field.Plots.Values]));
        }
    }
}
