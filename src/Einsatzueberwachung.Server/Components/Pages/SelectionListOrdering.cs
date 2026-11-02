using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Pages;

internal static class SelectionListOrdering
{
    public static IOrderedEnumerable<PersonalEntry> OrderPersonnelByFirstName(IEnumerable<PersonalEntry> personnel) =>
        personnel
            .OrderBy(person => person.Vorname, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(person => person.Nachname, StringComparer.CurrentCultureIgnoreCase);

    public static IOrderedEnumerable<DogSpecialization> OrderDogSpecializations(IEnumerable<DogSpecialization> specializations) =>
        specializations
            .Where(specialization => specialization != DogSpecialization.None)
            .OrderBy(GetDogSpecializationPriority)
            .ThenBy(specialization => specialization.GetDisplayName(), StringComparer.CurrentCultureIgnoreCase);

    private static int GetDogSpecializationPriority(DogSpecialization specialization) =>
        specialization switch
        {
            DogSpecialization.Flaechensuche => 0,
            DogSpecialization.Mantrailing => 1,
            DogSpecialization.Truemmersuche => 2,
            _ => 3
        };
}
