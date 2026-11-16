using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Components.Pages;

namespace Einsatzueberwachung.Tests;

public class EinsatzMonitorDogSelectionTests
{
    [Fact]
    public void ApplyDogSelection_WhenTeamNameIsEmpty_UsesDogNameAsInitialDefault()
    {
        var dog = new DogEntry
        {
            Id = "dog-1",
            Name = "Rex",
            HundefuehrerIds = ["handler-1"]
        };
        var form = new EinsatzMonitor.TeamEditorModel();

        EinsatzMonitor.ApplyDogSelection(form, [dog], dog.Id);

        Assert.Equal(dog.Id, form.DogId);
        Assert.Equal("handler-1", form.HundefuehrerId);
        Assert.Equal(dog.Name, form.TeamName);
    }

    [Fact]
    public void ApplyDogSelection_PreservesCustomTeamName()
    {
        var dog = new DogEntry
        {
            Id = "dog-1",
            Name = "Rex",
            HundefuehrerIds = ["handler-1"]
        };
        var form = new EinsatzMonitor.TeamEditorModel { TeamName = "Suchteam Alpha" };

        EinsatzMonitor.ApplyDogSelection(form, [dog], dog.Id);

        Assert.Equal("Suchteam Alpha", form.TeamName);
        Assert.Equal(dog.Id, form.DogId);
    }

    [Fact]
    public void ResolveTeamNameFromForm_UsesCustomNameForDogTeams()
    {
        var form = new EinsatzMonitor.TeamEditorModel
        {
            TeamType = "dog",
            TeamName = "Suchteam Alpha",
            DogId = "dog-1"
        };

        Assert.Equal("Suchteam Alpha", EinsatzMonitor.ResolveTeamNameFromForm(form));
    }
}
