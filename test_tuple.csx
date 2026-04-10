using System;
using System.Text.Json;

var tuple = (Latitude: 48.1, Longitude: 11.5);
(double Latitude, double Longitude)? nullable = tuple;

var opts = new JsonSerializerOptions { WriteIndented = true };
Console.WriteLine(""=== Tuple ==="" );
Console.WriteLine(JsonSerializer.Serialize(tuple, opts));
Console.WriteLine(""=== Nullable Tuple ==="" );
Console.WriteLine(JsonSerializer.Serialize(nullable, opts));
Console.WriteLine(""=== Deserialize back ==="" );
var json = JsonSerializer.Serialize(nullable, opts);
var back = JsonSerializer.Deserialize<(double Latitude, double Longitude)?>(json, opts);
Console.WriteLine(back?.Latitude + "" "" + back?.Longitude);
