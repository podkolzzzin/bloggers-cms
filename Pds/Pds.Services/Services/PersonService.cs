﻿using Microsoft.EntityFrameworkCore;
using Pds.Core.Enums;
using Pds.Core.Exceptions.Person;
using Pds.Data;
using Pds.Data.Entities;
using Pds.Services.Interfaces;
using Pds.Services.Models.Person;

namespace Pds.Services.Services;

public class PersonService : IPersonService
{
    private readonly IUnitOfWork unitOfWork;

    public PersonService(IUnitOfWork unitOfWork)
    {
        this.unitOfWork = unitOfWork;
    }

    public async Task<Person> GetAsync(Guid personId)
    {
        return await unitOfWork.Persons.GetFullByIdAsync(personId);
    }

    public async Task<List<Person>> GetAllAsync()
    {
        return await unitOfWork.Persons.GetAllFullAsync();
    }

    public async Task<Guid> CreateAsync(Person person)
    {
        if (person == null)
        {
            throw new ArgumentNullException(nameof(person));
        }

        if (person.Brands.Count == 0)
        {
            throw new PersonCreateException("Персону нельзя создать без бренда.");
        }

        // Restore brands from DB
        var brandsFromApi = person.Brands;
        var brandsFromBd = new List<Brand>();
        foreach (var brandFromApi in brandsFromApi)
        {
            var brandFromDb = await unitOfWork.Brands.GetFirstWhereAsync(c => c.Id == brandFromApi.Id);
            if (brandFromDb != null)
            {
                brandsFromBd.Add(brandFromDb);
            }
        }

        person.Brands = brandsFromBd;
        person.CreatedAt = DateTime.UtcNow;
        var result = await unitOfWork.Persons.InsertAsync(person);

        return result.Id;
    }

    public async Task<Guid> EditAsync(EditPersonModel model)
    {
        if (model == null)
        {
            throw new PersonEditException($"Модель запроса пуста.");
        }
            
        if (!model.Brands.Any(b =>b.IsSelected))
        {
            throw new PersonEditException("Персону нельзя создать без бренда.");
        }

        var person = await unitOfWork.Persons.GetFullByIdAsync(model.Id);
            
        if (person == null)
        {
            throw new PersonEditException($"Персона с id {model.Id} не найдена.");
        }

        if (person.Status == PersonStatus.Archived)
        {
            throw new PersonEditException($"Нельзя редактировать архивную персону.");
        }

        person.UpdatedAt = DateTime.UtcNow;
        person.FirstName = model.FirstName;
        person.LastName = model.LastName;
        person.ThirdName = model.ThirdName;
        person.Country = model.Country;
        person.City = model.City;
        person.Rate = model.Rate;
        person.Topics = model.Topics;
        person.Info = model.Info;

        person.Brands = new List<Brand>();
        foreach (var brandId in model.Brands.Where(b=>b.IsSelected).Select(b=>b.Id))
        {
            var brand = await unitOfWork.Brands.GetFirstWhereAsync(b => b.Id == brandId);
            person.Brands.Add(brand);
        }

        // Delete and update old resources
        foreach (var resource in person.Resources)
        {
            var resourceModel = model.Resources.FirstOrDefault(m=>m.Id == resource.Id);
            if (resourceModel == null)
            {
                person.Resources.Remove(resource);
            }
            else
            {
                resource.Name = resourceModel.Name;
                resource.Url = resourceModel.Url;
                resource.UpdatedAt = DateTime.UtcNow;
                unitOfWork.GetContextEntry(resource).State = EntityState.Modified;
            }
        }
        
        // Add new resources
        foreach (var newResourceModel in model.Resources.Where(r => r.Id == Guid.Empty))
        {
            var newResource = new Resource
            {
                CreatedAt = DateTime.UtcNow,
                Name = newResourceModel.Name,
                Url = newResourceModel.Url,
                PersonId = person.Id
            };

            person.Resources.Add(newResource);
        }

        var result = await unitOfWork.Persons.UpdateAsync(person);

        return result.Id;
    }

    public async Task ArchiveAsync(Guid personId)
    {
        var person = await unitOfWork.Persons.GetFirstWhereAsync(p => p.Id == personId);
        if (person != null)
        {
            person.Status = PersonStatus.Archived;
            person.ArchivedAt = DateTime.UtcNow;
            await unitOfWork.Persons.UpdateAsync(person);
        }
    }

    public async Task UnarchiveAsync(Guid personId)
    {
        var person = await unitOfWork.Persons.GetFirstWhereAsync(p => p.Id == personId);
        if (person is {Status: PersonStatus.Archived})
        {
            person.Status = PersonStatus.Active;
            person.UnarchivedAt = DateTime.UtcNow;
            await unitOfWork.Persons.UpdateAsync(person);
        }
    }

    public async Task DeleteAsync(Guid personId)
    {
        var person = await unitOfWork.Persons.GetFullByIdAsync(personId);
        if (person == null)
        {
            throw new PersonDeleteException("Персона не найдена");
        }

        if (person.Status == PersonStatus.Archived)
        {
            throw new PersonDeleteException("Нельзя заархивированную персону.");
        }

        if (person.Contents is { Count: > 0 })
        {
            throw new PersonDeleteException("Нельзя удалить персону с привязанным контентом.");
        }

        await unitOfWork.Persons.Delete(person);
    }

    public async Task<List<Person>> GetPersonsForListsAsync()
    {
        var persons = new List<Person> { new() { Id = Guid.Empty } };
        var personsFromDb = await unitOfWork.Persons.GetForListsAsync();
        persons.AddRange(personsFromDb);

        return persons;
    }
}