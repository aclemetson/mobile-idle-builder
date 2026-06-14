using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode tests for the manager bonus system:
    ///  - ManagerBonus.Bake/Unbake exactness (no float drift on apply→remove).
    ///  - Re-apply after a speed upgrade keeps the bonus relative to the new level.
    ///  - ProductionSystem honours an OutputQuantity manager's AppliedOutputMult at deposit time.
    /// </summary>
    [TestFixture]
    public class ManagerServiceTests
    {
        static ManagerSO MakeManager(ManagerBonusType type, float value, string id = "mgr_test")
        {
            var so = ScriptableObject.CreateInstance<ManagerSO>();
            so.id = id;
            so.bonusType = type;
            so.bonusValue = value;
            return so;
        }

        // ── Bake / Unbake exactness ───────────────────────────────────────────

        [Test]
        public void CraftSpeed_BakeMultipliesSpeed_UnbakeRestoresExactly()
        {
            var mgr = MakeManager(ManagerBonusType.CraftSpeed, 1.25f);
            var bd  = new BuildingData { ProductionSpeed = 4f };

            var data = ManagerBonus.Bake(ref bd, mgr, managerIndex: 3);

            Assert.AreEqual(5f, bd.ProductionSpeed, 0f, "CraftSpeed must multiply ProductionSpeed");
            Assert.AreEqual(4f, data.PreBonusSpeed, 0f, "pre-bonus speed must be captured");
            Assert.AreEqual(3,  data.ManagerIndex);
            Assert.AreEqual(1f, data.AppliedOutputMult, 0f);
            Assert.AreEqual(1f, data.AppliedPowerMult,  0f);

            ManagerBonus.Unbake(ref bd, data);
            Assert.AreEqual(4f, bd.ProductionSpeed, 0f, "Unbake must restore the exact pre-bonus speed");
        }

        [Test]
        public void OutputQuantity_DoesNotTouchSpeed_StoresMultiplier()
        {
            var mgr = MakeManager(ManagerBonusType.OutputQuantity, 3f);
            var bd  = new BuildingData { ProductionSpeed = 2f };

            var data = ManagerBonus.Bake(ref bd, mgr, 0);

            Assert.AreEqual(2f, bd.ProductionSpeed, 0f, "OutputQuantity must not change speed");
            Assert.AreEqual(3f, data.AppliedOutputMult, 0f);
            Assert.AreEqual(1f, data.AppliedPowerMult,  0f);
        }

        [Test]
        public void PowerDiscount_StoresKeptFraction_DoesNotTouchSpeed()
        {
            var mgr = MakeManager(ManagerBonusType.PowerDiscount, 0.8f);
            var bd  = new BuildingData { ProductionSpeed = 2f };

            var data = ManagerBonus.Bake(ref bd, mgr, 0);

            Assert.AreEqual(2f,  bd.ProductionSpeed, 0f);
            Assert.AreEqual(0.8f, data.AppliedPowerMult,  0f);
            Assert.AreEqual(1f,   data.AppliedOutputMult, 0f);
        }

        [Test]
        public void Upgrade_ResetsSpeedFromLevel_ThenReapplyKeepsBonus()
        {
            var mgr = MakeManager(ManagerBonusType.CraftSpeed, 1.5f);
            var bd  = new BuildingData { ProductionSpeed = 4f };

            // Initial assignment at level speed 4 → 6.
            var data = ManagerBonus.Bake(ref bd, mgr, 0);
            Assert.AreEqual(6f, bd.ProductionSpeed, 0f);

            // A speed upgrade overwrites ProductionSpeed with the clean level value (8), wiping the bonus.
            bd.ProductionSpeed = 8f;

            // Re-applying (the upgrade-site hook) bakes again using the new base.
            data = ManagerBonus.Bake(ref bd, mgr, 0);
            Assert.AreEqual(12f, bd.ProductionSpeed, 0f, "re-apply must keep the 1.5x relative to the new level");
            Assert.AreEqual(8f,  data.PreBonusSpeed, 0f);

            ManagerBonus.Unbake(ref bd, data);
            Assert.AreEqual(8f, bd.ProductionSpeed, 0f, "unassign after upgrade must restore the new level speed exactly");
        }

        // ── ProductionSystem OutputQuantity integration ───────────────────────

        [Test]
        public void ProductionSystem_OutputQuantityManager_MultipliesDepositedOutput()
        {
            using var world = new World("ManagerOutputTest");
            var em = world.EntityManager;

            var simGroup = world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(world.CreateSystem<ProductionSystem>());
            simGroup.SortSystems();

            const int inputItem = 1, outputItem = 4, inputQty = 2;

            var player = em.CreateEntity();
            em.AddComponent<PlayerInventoryTag>(player);
            var inv = em.AddBuffer<InventorySlot>(player);
            SlotBufferUtils.AddToInventory(ref inv, inputItem, inputQty);

            var building = em.CreateEntity();
            em.AddComponentData(building, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            em.AddComponentData(building, new RecipeProcessData
            {
                RecipeID = 1, CraftTime = 0.1f, Progress = 0.09f, IsCrafting = true
            });
            var inBuf = em.AddBuffer<RecipeInputSlot>(building);
            inBuf.Add(new RecipeInputSlot { ItemID = inputItem, Quantity = inputQty });
            var outBuf = em.AddBuffer<RecipeOutputSlot>(building);
            outBuf.Add(new RecipeOutputSlot { ItemID = outputItem, Quantity = 1 });
            em.AddBuffer<BuildingInputSlot>(building);
            em.AddBuffer<BuildingOutputSlot>(building);
            em.AddComponentData(building, new BuildingInventoryConfig { OutputCapacity = 20, InputCapacity = 20 });

            // Packrat-style 2x output manager assigned to this building.
            em.AddComponentData(building, new ManagerAssignmentData
            {
                ManagerIndex = 0, PreBonusSpeed = 1f, AppliedOutputMult = 2f, AppliedPowerMult = 1f
            });

            world.SetTime(new TimeData(elapsedTime: 0, deltaTime: 1f));
            world.Update();

            var localOut = em.GetBuffer<BuildingOutputSlot>(building);
            int produced = SlotBufferUtils.CountInOutputBuffer(localOut, outputItem);
            Assert.AreEqual(2, produced, "OutputQuantity 2x manager must deposit 2 output per recipe output of 1");
        }
    }
}
