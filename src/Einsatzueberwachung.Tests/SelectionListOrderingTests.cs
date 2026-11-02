using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components.Pages;

namespace Einsatzueberwachung.Tests;

public class SelectionListOrderingTests
{
    [Fact]
    public void OrderPersonnelByFirstName_UsesSurnameAsTieBreaker()
    {
        var personnel = new[]
        {
            new PersonalEntry { Id = "z", Vorname = "Zoe", Nachname = "Ziegler" },
            new PersonalEntry { Id = "a2", Vorname = "Anna", Nachname = "Meier" },
            new PersonalEntry { Id = "a1", Vorname = "Anna", Nachname = "Adler" }
        };

        var orderedIds = SelectionListOrdering.OrderPersonnelByFirstName(personnel).Select(person => person.Id);

        Assert.Equal(new[] { "a1", "a2", "z" }, orderedIds);
    }

    [Fact]
    public void OrderDogSpecializations_PrioritizesCoreTypesThenSortsRemainingNames()
    {
        var remaining = new[]
        {
            DogSpecialization.Wasserortung,
            DogSpecialization.Lawinensuche,
            DogSpecialization.Gelaendesuche,
            DogSpecialization.Leichensuche,
            DogSpecialization.InAusbildung
        }.OrderBy(specialization => specialization.GetDisplayName(), StringComparer.CurrentCultureIgnoreCase);

        var ordered = SelectionListOrdering.OrderDogSpecializations(Enum.GetValues<DogSpecialization>());

        Assert.Equal(
            new[]
            {
                DogSpecialization.Flaechensuche,
                DogSpecialization.Mantrailing,
                DogSpecialization.Truemmersuche
            }.Concat(remaining),
            ordered);
    }
}
