using NGPlugin.BoundarySystem;
using NGPlugin.BoundarySystem.Sectors;
using NGPlugin.Config;
using NGPlugin.Messages;
using NGPlugin.Scripts;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;
using VRage.Game;
using VRageMath;
using NGPlugin.Utilities;
using VRage.Game.Entity;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.Weapons.Guns;
using SpaceEngineers.Game.World;
using VRage.Utils;
using NLog;
using Sandbox.Game.World.Generator;

namespace NGPlugin.Scripts.ExampleScripts
{
    public class GridSpawnInSpace : ScriptAPI
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /*  Script Use:
         *      1. This will generate valid spawn point anywhere on the server this runs on inside a valid sector
         *      2. Several options to configurable spawning methods:
         */


        private bool SpawnNearFactionMembers = false; //Will attempt to spawn near faction members if unsectored. AWAITING IMPLEMENTATION
        private SpawnOption SelectedOption = SpawnOption.SpawnNearPlanets; //Select spawn method
        private byte TargetSectorID = 0; //Will force spawn inside a target sectorID (0 will get random sector)
        private float PlanetSpawnDistancePercentRadius = 1.50f; //Will generate a spawn distance percent radius increase from planet
        private int SpawnAttempts = 50; //Number of attempts to find a valid spawn position.






        /* Do NOT modify ANYTHING below */
        private Random rand = new Random(new Guid().GetHashCode());
        enum SpawnOption
        {
            SpawnNearPlanets,
            SpawnNearAsteroid,
            SpawnRandom,
            SpawnNearTradeStation //Not Implemented Yet
        }
        BoundingSphereD SearchArea;
        private long IdentityID;
        private ScriptSpawnMessage spawnReqMsg;
        private SectorAPI TargetSector;


        public async override Task InboundSpawnPad_Pre(ScriptSpawnMessage spawnReqMsg)
        {
            Log.Info($"GridSpawnInSpace");

            this.spawnReqMsg = spawnReqMsg;
            IdentityID = spawnReqMsg.playerOBData.First().IdentityID;
            if (!RegionHandler.TryGetServer(RegionHandler.ThisServer.ServerID, out ServerAPI foundServer))
                return;

            Log.Info($"GridSpawnInSpace");
            List<SectorAPI> allSectors = foundServer.ChildSectors;
            if (allSectors.Count != 0)
            {
                int randInt = rand.Next(allSectors.Count);
                TargetSector = allSectors[randInt];



                SearchArea = TargetSector.GetMaxBoundingSphereInside();
            }


            if (SearchArea == null || SearchArea.Radius == 0)
                SearchArea = new BoundingSphereD(Vector3D.Zero, 200000);

            Log.Info($"Script Search Area: {SearchArea.ToString()}");


            Log.Info($"Searching Method is: {SelectedOption}");
            switch (SelectedOption)
            {
                case SpawnOption.SpawnNearPlanets:
                    await TrySpawnNearPlanets();
                    break;

                case SpawnOption.SpawnNearAsteroid:
                    await TrySpawnNearAsteroid();
                    break;

                case SpawnOption.SpawnNearTradeStation:
                    Log.Fatal("SpawnNearTradeStation Not Implemented Yet!");
                    break;

                default:
                    GetRandomInSearch();
                    break;
            }

        }

        private async Task<bool> TrySpawnNearPlanets()
        {
            MyPlanet foundPlanet = null;


            await AsyncInvoke.InvokeAsync(() =>
            {
                foreach (MyEntity ent in MyEntities.GetEntitiesInSphere(ref SearchArea))
                {
                    if (ent.Parent != null)
                        continue;

                    if (ent is MyPlanet)
                    {
                        foundPlanet = ent as MyPlanet;
                        break;
                    }
                }
            });

            if (foundPlanet == null)
            {
                //Do something here
                return false;
            }

            

            Vector3D center = foundPlanet.PositionComp.WorldVolume.Center;
            for (int j = 0; j < SpawnAttempts; j++)
            {
                Vector3 vector = MyUtils.GetRandomVector3Normalized();
                if (vector.Dot(MySector.DirectionToSunNormalized) < 0f && j < 20)
                {
                    vector = -vector;
                }

                spawnReqMsg.GridSpawnPosition = center + vector * (foundPlanet.AverageRadius * (1 + PlanetSpawnDistancePercentRadius));

                //Verify position is in sector
                if (SearchArea.Contains(spawnReqMsg.GridSpawnPosition) == ContainmentType.Disjoint || IsPositionInChildren(TargetSector, spawnReqMsg.GridSpawnPosition))
                {
                    continue;
                }
                else
                {
                    Log.Info($"Found Planet: {foundPlanet.StorageName} Spawning @ {spawnReqMsg.GridSpawnPosition.ToString()}");
                    return true;
                }
            }


            return false;
        }

        private bool TryFindPositionNearFaction(out List<Vector3D> validPositions)
        {
            validPositions = new List<Vector3D>();
            MyFaction foundfac = MySession.Static.Factions.GetPlayerFaction(IdentityID);
            if (foundfac == null)
                return false;

            foreach (var member in foundfac.Members)
            {
                MyIdentity player = MySession.Static.Players.TryGetIdentity(member.Key);

                //Ignore yourself dipshit
                if (player.IdentityId == IdentityID)
                    continue;

                MyCharacter character = player.Character;
                if (character != null && !character.IsDead && !character.MarkedForClose)
                {
                    validPositions.Add(character.PositionComp.GetPosition());
                }
            }

            return validPositions.Count > 0;
        }

        private async Task<bool> TrySpawnNearAsteroid()
        {
            //Keen has settings?
            float optimalSpawnDistance = MySession.Static.Settings.OptimalSpawnDistance;
            float num = (optimalSpawnDistance - optimalSpawnDistance * 0.5f) * 0.9f;


            for (int j = 0; j < SpawnAttempts; j++)
            {
                Log.Info($"Searching Asteroids {SearchArea.Radius}m @ {SearchArea.Center.ToString()}");
                Vector3D? vector3D4 = null;

                await AsyncInvoke.InvokeAsync(() =>
                {
                    vector3D4 = MyProceduralWorldModule.FindFreeLocationCloseToAsteroid(SearchArea, null, true, false, 100, num, out _, out _);
                });

                if (vector3D4.HasValue && !IsPositionInChildren(TargetSector, vector3D4.Value))
                {
                    spawnReqMsg.GridSpawnPosition = vector3D4.Value;
                    Log.Info($"Found Asteroid! Spawning @ {spawnReqMsg.GridSpawnPosition.ToString()}");
                    return true;
                }

            }

            Log.Info($"Failed to SpawnNear Asteroid!");

            return false;
        }

        private bool IsPositionInChildren(SectorAPI sector, Vector3D target)
        {
            if (!RegionHandler.ThisServer.HasSectors)
                return false;

            var node = RegionHandler.ThisCluster.SectorTree.FindTreeNode(x => x.Data.SectorID == sector.SectorID);
            return (node.Children != null) && node.Children.Any(x => x.Data.Contains(target));
        }

        private bool GetRandomInSearch()
        {
            for (int j = 0; j < SpawnAttempts; j++)
            {
                Vector3 randVector = MyUtils.GetRandomVector3Normalized();
                Vector3D target = SearchArea.RandomToUniformPointInSphere(randVector.X, randVector.Y, randVector.Z);

                if (!IsPositionInChildren(TargetSector, target))
                {
                    spawnReqMsg.GridSpawnPosition = target;
                    Log.Info($"Found Random Position! Spawning @ {spawnReqMsg.GridSpawnPosition.ToString()}");
                    return true;
                }
            }

            return false;
        }
    }
}
