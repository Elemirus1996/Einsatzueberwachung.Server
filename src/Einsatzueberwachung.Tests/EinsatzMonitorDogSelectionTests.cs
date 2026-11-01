using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Components.Pages;

namespace Einsatzueberwachung.Tests;

public class EinsatzMonitorDogSelectionTests
{
    [Fact]
    public void ApplyDogSelection_WhenDogIsSelectedFirst_SetsDogHandlerAndTeamNameTogether()
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
}
