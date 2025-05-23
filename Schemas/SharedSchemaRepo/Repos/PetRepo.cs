﻿using static System.Runtime.InteropServices.JavaScript.JSType;
using System;

namespace SharedSchemaRepo;
// This partial class demonstrates how you can extend the generated code in 
// various ways.
// - Extended constructor to take other repos as parameters
// - Implement a new method to seed the database

// Extend the IPetRepo interface so new method can be seen by module(s).
public partial interface IPetRepo : IDocumentRepo<Pet>
{
    public Task<ActionResult> SeedAsync(ICallerInfo callerInfo, int numPets);
}
// Extend the PetRepo class to implement the new constructor and method.
public partial class PetRepo : DYDBRepository<Pet>, IPetRepo
{
    // Implement a constructor that takes the tag and category repos as parameters,
    // Microsoft DIwill select the constructor with the most parameters it can satisfy,
    // so this constructor will be used in lieu of the constructed defined in the
    // generated code.
    public PetRepo(IAmazonDynamoDB client, ICategoryRepo categoryRepo, ITagRepo tagRepo) : base(client) { 
        this.tagRepo = tagRepo; 
        this.categoryRepo = categoryRepo;
        UseNotifications = false;
        NotificationsSqsQueue = "notifications";
    }

    private ITagRepo tagRepo;
    private ICategoryRepo categoryRepo; 

    // Implement the new method to satisfy the interface
    public async Task<ActionResult> SeedAsync(ICallerInfo callerInfo, int numPets )
    {

        List<string> petNames = ["Luna", "Max", "Bella", "Charlie", "Lucy", "Cooper", "Daisy", "Milo", "Lily", "Oliver", "Molly", "Rocky", "Bailey", "Shadow", "Sophie", "Tucker", "Coco", "Bear", "Maggie", "Leo",
"Ruby", "Oscar", "Sadie", "Zeus", "Penny", "Duke", "Chloe", "Winston", "Rosie", "Jack", "Lola", "Buddy", "Gracie", "Thor", "Nala", "Scout", "Hazel", "Bruno", "Millie", "Sam",
"Nova", "Bentley", "Piper", "Rex", "Pearl", "Atlas", "Willow", "Finn", "Maya", "Moose", "Pepper", "Ziggy", "Roxy", "Felix", "Ginger", "Koda", "Belle", "Blue", "Stella", "Banjo",
"Maple", "Louie", "Winnie", "Jasper", "Poppy", "Diesel", "Olive", "River", "Sage", "Simba", "Luna", "Archie", "Juniper", "Ozzy", "Pixie", "Hero", "Ivy", "Tank", "Pip", "Odin",
"Birdie", "Harvey", "Clover", "Rocket", "Nutmeg", "Storm", "Fiona", "Zigzag", "Echo", "Bolt", "Coral", "Phoenix", "Iris", "Theo", "Luna", "Atlas", "Nova", "Cedar", "Sage", "Ash"];

        var categoriesResult = await categoryRepo.ListAsync(callerInfo);
        var categories = (List<Category>)categoriesResult!.Value!;

        var tagResult = await tagRepo.ListAsync(callerInfo);
        var tags = (List<Tag>)tagResult!.Value!;

        var pets = new List<Pet>();
        var maxPets = numPets;
        Random randomPetName = new Random();   
        Random randomCategory = new Random();   
        Random randomTag = new Random();
        Random numberOfTags = new Random(); 
        for (int i = 0; i < maxPets; i++)
        {
            var petTags = new List<string>();
            var numTags = numberOfTags.Next(0, tags.Count + 1);
            for (int t = 0; t < numTags; t++)
            {
                string tag = "";
                do
                {
                    tag = tags[randomTag.Next(0, tags.Count)].Name;
                } while (petTags.Contains(tag));

                petTags.Add(tag);
            }

            var pet = new Pet
            {
                Id = Guid.NewGuid().ToString(),
                Name = petNames[randomPetName.Next(0,petNames.Count)],
                Category = categories[randomCategory.Next(0,categories.Count)].Id,
                Tags = petTags,
                PhotoUrls = new List<string> { "" },
                PetStatus = PetStatus.Available,
                CreateUtcTick = DateTime.UtcNow.Ticks,
                UpdateUtcTick = DateTime.UtcNow.Ticks
            };
            pets.Add(pet);
        }
        foreach(var pet in pets)
        {
            await CreateAsync(callerInfo, pet);
        }

        return new StatusCodeResult(200);
    }

}
