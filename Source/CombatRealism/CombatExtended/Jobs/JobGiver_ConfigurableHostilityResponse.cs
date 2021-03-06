﻿using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CombatExtended
{
    public class JobGiver_ConfigurableHostilityResponse : ThinkNode_JobGiver
    {
        private static List<Thing> tmpThreats = new List<Thing>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.playerSettings == null || !pawn.playerSettings.UsesConfigurableHostilityResponse)
            {
                return null;
            }
            if (PawnUtility.PlayerForcedJobNowOrSoon(pawn))
            {
                return null;
            }
            switch (pawn.playerSettings.hostilityResponse)
            {
                case HostilityResponseMode.Ignore:
                    return null;
                case HostilityResponseMode.Attack:
                    return TryGetAttackNearbyEnemyJob(pawn);
                case HostilityResponseMode.Flee:
                    return TryGetFleeJob(pawn);
                default:
                    return null;
            }
        }

        private Job TryGetAttackNearbyEnemyJob(Pawn pawn)
        {
            if (pawn.story != null && pawn.story.WorkTagIsDisabled(WorkTags.Violent))
            {
                return null;
            }
            bool flag = pawn.equipment.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon;
            float num = 8f;
            if (!flag)
            {
                num = Mathf.Clamp(pawn.equipment.PrimaryEq.PrimaryVerb.verbProps.range * 0.66f, 2f, 20f);
            }
            float maxDist = num;
            Thing thing = AttackTargetFinder.BestAttackTarget(pawn, TargetScanFlags.NeedLOSToPawns | TargetScanFlags.NeedLOSToNonPawns | TargetScanFlags.NeedReachable | TargetScanFlags.NeedThreat, null, 0f, maxDist, default(IntVec3), 3.40282347E+38f, false);
            // TODO evaluate if this is necessary?
            Pawn o = thing as Pawn;
            if (o != null) if
                    (o.Downed || o.health.InPainShock)
                {
                    return null;
                }

            if (thing == null)
            {
                return null;
            }
            if (flag || thing.Position.AdjacentTo8Way(pawn.Position))
            {
                return new Job(JobDefOf.AttackMelee, thing);
            }

            // Check for reload before attacking
            Verb verb = pawn.TryGetAttackVerb();
            if (pawn.equipment.PrimaryEq != null && verb != null && verb == pawn.equipment.PrimaryEq.PrimaryVerb)
            {
                if (pawn.equipment.Primary != null)
                {
                    CompAmmoUser compAmmo = pawn.equipment.Primary.TryGetComp<CompAmmoUser>();
                    if (compAmmo != null)
                    {
                        if (!compAmmo.hasMagazine)
                        {
                            if (compAmmo.useAmmo && !compAmmo.hasAmmo)
                                return new Job(JobDefOf.AttackMelee, thing);
                        }
                        else if (compAmmo.curMagCount <= 0)
                        {
                            Job job = new Job(CE_JobDefOf.ReloadWeapon, pawn, pawn.equipment.Primary);
                            if (job != null)
                                return job;
                        }
                    }
                }
            }

            return new Job(JobDefOf.AttackStatic, thing);
        }

        private Job TryGetFleeJob(Pawn pawn)
        {
            if (!SelfDefenseUtility.ShouldStartFleeing(pawn))
            {
                return null;
            }
            tmpThreats.Clear();
            List<IAttackTarget> potentialTargetsFor = pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn);
            for (int i = 0; i < potentialTargetsFor.Count; i++)
            {
                IAttackTarget attackTarget = potentialTargetsFor[i];
                if (!attackTarget.ThreatDisabled())
                {
                    tmpThreats.Add((Thing)attackTarget);
                }
            }
            if (!tmpThreats.Any())
            {
                Log.Warning(pawn.LabelShort + " decided to flee but there is no any threat around.");
                return null;
            }
            IntVec3 fleeDest = GetFleeDest(pawn, tmpThreats);
            tmpThreats.Clear();
            return new Job(JobDefOf.FleeAndCower, fleeDest);
        }

        private IntVec3 GetFleeDest(Pawn pawn, List<Thing> threats)
        {
			IntVec3 bestPos = pawn.Position;
			float bestScore = -1f;
			TraverseParms traverseParms = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false);
			RegionTraverser.BreadthFirstTraverse(pawn.GetRegion(), (Region from, Region reg) => reg.Allows(traverseParms, false), delegate(Region reg)
			{
                Danger danger = reg.DangerFor(pawn);
                foreach (IntVec3 current in reg.Cells)
                {
                    if (current.Standable(pawn.Map))
                    {
                        if (reg.portal == null)
                        {
                            Thing thing = null;
                            float num = 0f;
                            for (int i = 0; i < threats.Count; i++)
                            {
                                float num2 = current.DistanceToSquared(threats[i].Position);
                                if (thing == null || num2 < num)
                                {
                                    thing = threats[i];
                                    num = num2;
                                }
                            }
                            float num3 = Mathf.Sqrt(num);
                            float f = Mathf.Min(num3, 23f);
                            float num4 = Mathf.Pow(f, 1.2f);
                            num4 *= Mathf.InverseLerp(50f, 0f, (current - pawn.Position).LengthHorizontal);
                            if (current.GetRoom(pawn.Map) != thing.GetRoom())
                            {
                                num4 *= 4.2f;
                            }
                            else if (num3 < 8f)
                            {
                                num4 *= 0.05f;
                            }
                            if (pawn.Map.pawnDestinationManager.DestinationIsReserved(current, pawn))
                            {
                                num4 *= 0.5f;
                            }
                            if (danger == Danger.Deadly)
                            {
                                num4 *= 0.8f;
                            }
                            if (num4 > bestScore)
                            {
                                bestPos = current;
                                bestScore = num4;
                            }
                        }
                    }
                }
                return false;
            }, 20);
            return bestPos;
        }
    }
}
