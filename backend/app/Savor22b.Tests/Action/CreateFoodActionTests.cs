namespace Savor22b.Tests.Action;

using System;
using System.Collections.Immutable;
using Libplanet.State;
using Savor22b.Action;
using Savor22b.Action.Exceptions;
using Savor22b.Model;
using Savor22b.States;
using Xunit;


public class CreateFoodActionTests : ActionTests
{
    private Recipe getRecipeById(int recipeID)
    {
        var recipe = CsvDataHelper.GetRecipeById(recipeID);

        if (recipe is null)
        {
            throw new Exception();
        }

        return recipe;
    }

    private Recipe getRandomRecipeWithEquipmentCategory(string category)
    {
        var recipeId = 1;
        Recipe? recipe = null;

        while (recipe is null)
        {
            var recipeCandidate = CsvDataHelper.GetRecipeById(recipeId);
            recipeId++;
            if (recipeCandidate is null)
            {
                throw new Exception($"{recipeId} does not exist");
            }

            if (recipeCandidate.RequiredKitchenEquipmentCategoryList.Count == 0)
            {
                continue;
            }

            foreach (var equipmentCategoryId in recipeCandidate.RequiredKitchenEquipmentCategoryList)
            {
                var kitchenEquipmentCategory = CsvDataHelper.GetKitchenEquipmentCategoryByID(equipmentCategoryId);
                if (kitchenEquipmentCategory is null)
                {
                    throw new Exception($"{equipmentCategoryId}");
                }

                if (kitchenEquipmentCategory.Category == category)
                {
                    recipe = recipeCandidate;
                }
            }
        }

        return recipe!;
    }

    private List<RefrigeratorState> generateMaterials(ImmutableList<int> IngredientIDList, ImmutableList<int> FoodIDList)
    {
        var RefrigeratorItemList = new List<RefrigeratorState>();
        foreach (var ingredientID in IngredientIDList)
        {
            RefrigeratorItemList.Add(
                RefrigeratorState.CreateIngredient(
                    Guid.NewGuid(),
                    ingredientID,
                    "D",
                    1,
                    1,
                    1,
                    1
                ));
        }
        foreach (var foodID in FoodIDList)
        {
            RefrigeratorItemList.Add(
                RefrigeratorState.CreateFood(
                    Guid.NewGuid(),
                    foodID,
                    "D",
                    1,
                    1,
                    1,
                    1,
                    1
                ));
        }

        return RefrigeratorItemList;
    }

    private (RootState, List<Guid>, List<int>) createPreset(Recipe recipe)
    {
        int spaceNumber = 1;
        List<Guid> kitchenEquipmentsToUse = new List<Guid>();
        List<int> spaceNumbersToUse = new List<int>();

        InventoryState inventoryState = new InventoryState();
        foreach (var item in generateMaterials(recipe.IngredientIDList, recipe.FoodIDList))
        {
            inventoryState = inventoryState.AddRefrigeratorItem(item);
        }

        KitchenState resultKitchenState = new KitchenState();

        foreach (var equipmentCategoryId in recipe.RequiredKitchenEquipmentCategoryList)
        {
            KitchenEquipmentCategory? kitchenEquipmentCategory = CsvDataHelper.GetKitchenEquipmentCategoryByID(equipmentCategoryId);

            if (kitchenEquipmentCategory == null)
            {
                throw new Exception();
            }

            var kitchenEquipments = CsvDataHelper.GetAllKitchenEquipmentByCategoryId(equipmentCategoryId);
            var kitchenEquipmentState = new KitchenEquipmentState(
                    Guid.NewGuid(),
                    kitchenEquipments[0].ID,
                    equipmentCategoryId
                );
            inventoryState = inventoryState.AddKitchenEquipmentItem(kitchenEquipmentState);

            if (kitchenEquipmentCategory.Category == "main")
            {
                resultKitchenState.InstallKitchenEquipment(kitchenEquipmentState, spaceNumber);
                spaceNumbersToUse.Add(spaceNumber);
                spaceNumber++;
            }
            else
            {
                kitchenEquipmentsToUse.Add(kitchenEquipmentState.StateID);
            }
        }

        RootState rootState = new RootState(
            inventoryState,
            new VillageState(new HouseState(1, 1, 1, resultKitchenState)));

        return (rootState, kitchenEquipmentsToUse, spaceNumbersToUse);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void Execute_Success_Normal(int recipeID)
    {
        var recipe = getRecipeById(recipeID);
        var blockIndex = 1;

        IAccountStateDelta beforeState = new DummyState();
        var (beforeRootState, kitchenEquipmentStateIdsToUse, spaceNumbersToUse) = createPreset(recipe);
        beforeState = beforeState.SetState(SignerAddress(), beforeRootState.Serialize());

        var newFoodGuid = Guid.NewGuid();
        var action = new CreateFoodAction(
            recipe.ID,
            newFoodGuid,
            (from stateList in beforeRootState.InventoryState.RefrigeratorStateList
             select stateList.StateID).ToList(),
            kitchenEquipmentStateIdsToUse,
            spaceNumbersToUse);

        var afterState = action.Execute(new DummyActionContext
        {
            PreviousStates = beforeState,
            Signer = SignerAddress(),
            Random = random,
            Rehearsal = false,
            BlockIndex = blockIndex,
        });

        var rootStateEncoded = afterState.GetState(SignerAddress());
        RootState rootState = rootStateEncoded is Bencodex.Types.Dictionary bdict
            ? new RootState(bdict)
            : throw new Exception();
        InventoryState afterInventoryState = rootState.InventoryState;

        Assert.Single(afterInventoryState.RefrigeratorStateList);
        Assert.Equal(recipe.ResultFoodID, afterInventoryState.RefrigeratorStateList[0].FoodID);
        Assert.Equal(blockIndex + recipe.RequiredBlock, afterInventoryState.RefrigeratorStateList[0].AvailableBlockIndex);
        foreach(var kitchenEquipmentStateId in kitchenEquipmentStateIdsToUse)
        {
            Assert.Equal(
                beforeRootState.InventoryState.GetKitchenEquipmentState(kitchenEquipmentStateId),
                afterInventoryState.GetKitchenEquipmentState(kitchenEquipmentStateId));
            Assert.True(afterInventoryState.GetKitchenEquipmentState(kitchenEquipmentStateId)!.IsInUse(blockIndex));
            Assert.False(afterInventoryState.GetKitchenEquipmentState(kitchenEquipmentStateId)!.IsInUse(blockIndex + recipe.RequiredBlock));
        }
        foreach(var spaceNumber in spaceNumbersToUse)
        {
            switch (spaceNumber)
            {
                case 1:
                    Assert.Equal(
                        beforeRootState.VillageState!.HouseState.KitchenState.FirstApplianceSpace,
                        rootState.VillageState!.HouseState.KitchenState.FirstApplianceSpace);
                    Assert.True(rootState.VillageState!.HouseState.KitchenState.FirstApplianceSpace.IsInUse(blockIndex));
                    Assert.False(rootState.VillageState!.HouseState.KitchenState.FirstApplianceSpace.IsInUse(blockIndex + recipe.RequiredBlock));
                    break;
                case 2:
                    Assert.Equal(
                        beforeRootState.VillageState!.HouseState.KitchenState.SecondApplianceSpace,
                        rootState.VillageState!.HouseState.KitchenState.SecondApplianceSpace);
                    Assert.True(rootState.VillageState!.HouseState.KitchenState.SecondApplianceSpace.IsInUse(blockIndex));
                    Assert.False(rootState.VillageState!.HouseState.KitchenState.SecondApplianceSpace.IsInUse(blockIndex + recipe.RequiredBlock));
                    break;
                case 3:
                    Assert.Equal(
                        beforeRootState.VillageState!.HouseState.KitchenState.ThirdApplianceSpace,
                        rootState.VillageState!.HouseState.KitchenState.ThirdApplianceSpace);
                    Assert.True(rootState.VillageState!.HouseState.KitchenState.ThirdApplianceSpace.IsInUse(blockIndex));
                    Assert.False(rootState.VillageState!.HouseState.KitchenState.ThirdApplianceSpace.IsInUse(blockIndex + recipe.RequiredBlock));
                    break;
                default:
                    throw new Exception("");
            }
        }
    }

    // 내가 넣은 state id들만 사용된건지 체크하는 테스트 추가 필요

    // [Theory]
    // [InlineData(1)]
    // [InlineData(2)]
    // [InlineData(3)]
    // public void Execute_Success_ExistsMoreMaterials(int recipeID)
    // {

    //     Assert.Equal(3, afterInventoryState.RefrigeratorStateList.Count);
    //     Assert.Equal(afterInventoryState.RefrigeratorStateList[0].FoodID, recipe.ResultFoodID);
    //     Assert.Equal(true, afterInventoryState.KitchenEquipmentStateList[0].IsInUse);
    // }

    // [Fact]
    // public void Execute_Failure_AlreadyUsingEquipment()
    // {
    //     Assert.Throws<AlreadyUsingEquipmentException>(() =>
    //     {
    //         action.Execute(new DummyActionContext
    //         {
    //             PreviousStates = beforeState,
    //             Signer = SignerAddress(),
    //             Random = random,
    //             Rehearsal = false,
    //             BlockIndex = 1,
    //         });
    //     });
    // }

    [Fact]
    public void Execute_Failure_NotFoundKitchenEquipmentState()
    {
        var blockIndex = 1;
        Recipe recipe = getRandomRecipeWithEquipmentCategory("sub");

        IAccountStateDelta beforeState = new DummyState();
        var (beforeRootState, kitchenEquipmentStateIdsToUse, spaceNumbersToUse) = createPreset(recipe);
        var edibleStateIdsToUse = (from stateList in beforeRootState.InventoryState.RefrigeratorStateList
                                   select stateList.StateID).ToList();
        beforeRootState.SetInventoryState(beforeRootState.InventoryState.RemoveKitchenEquipmentItem(kitchenEquipmentStateIdsToUse[0]));
        beforeState = beforeState.SetState(SignerAddress(), beforeRootState.Serialize());

        var newFoodGuid = Guid.NewGuid();
        var action = new CreateFoodAction(
            recipe.ID,
            newFoodGuid,
            edibleStateIdsToUse,
            kitchenEquipmentStateIdsToUse,
            spaceNumbersToUse);

        Assert.Throws<NotFoundDataException>(() =>
        {
            action.Execute(new DummyActionContext
            {
                PreviousStates = beforeState,
                Signer = SignerAddress(),
                Random = random,
                Rehearsal = false,
                BlockIndex = blockIndex,
            });
        });
    }

    [Fact]
    public void Execute_Failure_NotFoundInstalledKitchenEquipmentState()
    {
        var blockIndex = 1;
        Recipe recipe = getRandomRecipeWithEquipmentCategory("main");

        IAccountStateDelta beforeState = new DummyState();
        var (beforeRootState, kitchenEquipmentStateIdsToUse, spaceNumbersToUse) = createPreset(recipe);
        foreach(var spaceNumber in spaceNumbersToUse)
        {
            beforeRootState.VillageState!.HouseState.KitchenState.GetApplianceSpaceStateByNumber(spaceNumber).UnInstallKitchenEquipment();
        }
        beforeState = beforeState.SetState(SignerAddress(), beforeRootState.Serialize());

        var newFoodGuid = Guid.NewGuid();
        var action = new CreateFoodAction(
            recipe.ID,
            newFoodGuid,
            (from stateList in beforeRootState.InventoryState.RefrigeratorStateList
             select stateList.StateID).ToList(),
            kitchenEquipmentStateIdsToUse,
            spaceNumbersToUse);

        Assert.Throws<NotFoundDataException>(() =>
        {
            action.Execute(new DummyActionContext
            {
                PreviousStates = beforeState,
                Signer = SignerAddress(),
                Random = random,
                Rehearsal = false,
                BlockIndex = blockIndex,
            });
        });
    }

    [Fact]
    public void Execute_Failure_NotFoundEdibleState()
    {
        var blockIndex = 1;
        Recipe recipe = getRecipeById(1);

        IAccountStateDelta beforeState = new DummyState();
        var (beforeRootState, kitchenEquipmentStateIdsToUse, spaceNumbersToUse) = createPreset(recipe);
        var edibleStateIdsToUse = (from stateList in beforeRootState.InventoryState.RefrigeratorStateList
                                   select stateList.StateID).ToList();
        beforeRootState.SetInventoryState(beforeRootState.InventoryState.RemoveRefrigeratorItem(beforeRootState.InventoryState.RefrigeratorStateList[0].StateID));
        beforeState = beforeState.SetState(SignerAddress(), beforeRootState.Serialize());

        var newFoodGuid = Guid.NewGuid();
        var action = new CreateFoodAction(
            recipe.ID,
            newFoodGuid,
            edibleStateIdsToUse,
            kitchenEquipmentStateIdsToUse,
            spaceNumbersToUse);

        Assert.Throws<NotFoundDataException>(() =>
        {
            action.Execute(new DummyActionContext
            {
                PreviousStates = beforeState,
                Signer = SignerAddress(),
                Random = random,
                Rehearsal = false,
                BlockIndex = blockIndex,
            });
        });
    }

    [Fact]
    public void Execute_Failure_NotHaveRequiredKitchenEquipmentState()
    {
        var blockIndex = 1;
        Recipe recipe = getRandomRecipeWithEquipmentCategory("sub");

       IAccountStateDelta beforeState = new DummyState();
        var (beforeRootState, kitchenEquipmentStateIdsToUse, spaceNumbersToUse) = createPreset(recipe);
        var edibleStateIdsToUse = (from stateList in beforeRootState.InventoryState.RefrigeratorStateList
                                   select stateList.StateID).ToList();
        beforeRootState.SetInventoryState(beforeRootState.InventoryState.RemoveKitchenEquipmentItem(kitchenEquipmentStateIdsToUse[0]));
        beforeRootState.SetInventoryState(beforeRootState.InventoryState.AddKitchenEquipmentItem(
            new KitchenEquipmentState(kitchenEquipmentStateIdsToUse[0], -1, -1)));
        beforeState = beforeState.SetState(SignerAddress(), beforeRootState.Serialize());

        var newFoodGuid = Guid.NewGuid();
        var action = new CreateFoodAction(
            recipe.ID,
            newFoodGuid,
            edibleStateIdsToUse,
            kitchenEquipmentStateIdsToUse,
            spaceNumbersToUse);

        Assert.Throws<NotHaveRequiredException>(() =>
        {
            action.Execute(new DummyActionContext
            {
                PreviousStates = beforeState,
                Signer = SignerAddress(),
                Random = random,
                Rehearsal = false,
                BlockIndex = blockIndex,
            });
        });
    }

    [Fact]
    public void Execute_Failure_NotHaveRequiredInstalledKitchenEquipmentState()
    {
        var blockIndex = 1;
        Recipe recipe = getRandomRecipeWithEquipmentCategory("main");

        IAccountStateDelta beforeState = new DummyState();
        var (beforeRootState, kitchenEquipmentStateIdsToUse, spaceNumbersToUse) = createPreset(recipe);
        foreach(var spaceNumber in spaceNumbersToUse)
        {
            var illusionKitchenEquipment = new KitchenEquipmentState(Guid.NewGuid(), -1, -1);
            beforeRootState.SetInventoryState(beforeRootState.InventoryState.AddKitchenEquipmentItem(illusionKitchenEquipment));
            beforeRootState.VillageState!.HouseState.KitchenState.InstallKitchenEquipment(illusionKitchenEquipment, spaceNumber);
        }
        var edibleStateIdsToUse = (from stateList in beforeRootState.InventoryState.RefrigeratorStateList
                                   select stateList.StateID).ToList();
        beforeState = beforeState.SetState(SignerAddress(), beforeRootState.Serialize());

        var newFoodGuid = Guid.NewGuid();
        var action = new CreateFoodAction(
            recipe.ID,
            newFoodGuid,
            edibleStateIdsToUse,
            kitchenEquipmentStateIdsToUse,
            spaceNumbersToUse);

        Assert.Throws<NotHaveRequiredException>(() =>
        {
            action.Execute(new DummyActionContext
            {
                PreviousStates = beforeState,
                Signer = SignerAddress(),
                Random = random,
                Rehearsal = false,
                BlockIndex = blockIndex,
            });
        });
    }

    [Fact]
    public void Execute_Failure_NotHaveRequiredEdibleState()
    {
        var blockIndex = 1;
        Recipe recipe = getRecipeById(1);

        IAccountStateDelta beforeState = new DummyState();
        var (beforeRootState, kitchenEquipmentStateIdsToUse, spaceNumbersToUse) = createPreset(recipe);
        beforeRootState.SetInventoryState(beforeRootState.InventoryState.RemoveRefrigeratorItem(beforeRootState.InventoryState.RefrigeratorStateList[0].StateID));
        beforeRootState.SetInventoryState(beforeRootState.InventoryState.AddRefrigeratorItem(
            RefrigeratorState.CreateIngredient(Guid.NewGuid(), -1, "A", 1, 1, 1, 1)));
        var edibleStateIdsToUse = (from stateList in beforeRootState.InventoryState.RefrigeratorStateList
                                   select stateList.StateID).ToList();
        beforeState = beforeState.SetState(SignerAddress(), beforeRootState.Serialize());

        var newFoodGuid = Guid.NewGuid();
        var action = new CreateFoodAction(
            recipe.ID,
            newFoodGuid,
            edibleStateIdsToUse,
            kitchenEquipmentStateIdsToUse,
            spaceNumbersToUse);

        Assert.Throws<NotHaveRequiredException>(() =>
        {
            action.Execute(new DummyActionContext
            {
                PreviousStates = beforeState,
                Signer = SignerAddress(),
                Random = random,
                Rehearsal = false,
                BlockIndex = blockIndex,
            });
        });
    }
}