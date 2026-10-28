using FluentValidation;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Validators;

/// <summary>
/// Validator für AppSettings
/// </summary>
public class AppSettingsValidator : AbstractValidator<AppSettings>
{
    public AppSettingsValidator()
    {
        RuleFor(s => s.DefaultFirstWarningMinutes)
            .GreaterThan(0).WithMessage("1. Timer-Warnung muss größer als 0 sein")
            .LessThanOrEqualTo(90).WithMessage("1. Timer-Warnung sollte nicht mehr als 90 Minuten sein");

        RuleFor(s => s.DefaultSecondWarningMinutes)
            .GreaterThan(0).WithMessage("2. Timer-Warnung muss größer als 0 sein")
            .LessThanOrEqualTo(120).WithMessage("2. Timer-Warnung sollte nicht mehr als 120 Minuten sein")
            .GreaterThan(s => s.DefaultFirstWarningMinutes)
            .WithMessage("2. Timer-Warnung muss größer als 1. Timer-Warnung sein");

        RuleFor(s => s.ThemeMode)
            .NotEmpty().WithMessage("Theme-Modus ist erforderlich");

        RuleFor(s => s.CollarNoSignalTimeoutSeconds)
            .InclusiveBetween(10, 600)
            .WithMessage("Kein-Signal-Zeitfenster muss zwischen 10 und 600 Sekunden liegen");

        RuleFor(s => s.PhoneNoSignalTimeoutSeconds)
            .InclusiveBetween(60, 1800)
            .WithMessage("Handy-Funkstille muss zwischen 1 und 30 Minuten liegen");

        RuleFor(s => s.TraccarGenauigkeit)
            .Must(g => g is "highest" or "high" or "medium" or "low")
            .WithMessage("Unbekannte Traccar-Genauigkeit");
        RuleFor(s => s.TraccarIntervallSekunden)
            .InclusiveBetween(5, 300)
            .WithMessage("Traccar-Intervall muss zwischen 5 und 300 Sekunden liegen");
        RuleFor(s => s.TraccarMindestabstandMeter)
            .InclusiveBetween(0, 500)
            .WithMessage("Traccar-Mindestabstand muss zwischen 0 und 500 Metern liegen");
    }
}
