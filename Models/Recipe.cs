namespace TempControl.Models;

/// <summary>Persisted process parameter set identified by the PLC recipe mode.</summary>
public sealed class Recipe
{
    public long Id { get; set; }
    public string RecipeName { get; set; } = string.Empty;
    public int RecipeMode { get; set; }

    public double Step1Zone1Temp { get; set; }
    public double Step1Zone2Temp { get; set; }
    public double Step1SoakTime { get; set; }
    public double Step1RampRate { get; set; }
    public double Step2Zone1Temp { get; set; }
    public double Step2Zone2Temp { get; set; }
    public double Step2SoakTime { get; set; }
    public double Step2RampRate { get; set; }
    public double Step3Zone1Temp { get; set; }
    public double Step3Zone2Temp { get; set; }
    public double Step3SoakTime { get; set; }
    public double Step3RampRate { get; set; }
    public double Step4Zone1Temp { get; set; }
    public double Step4Zone2Temp { get; set; }
    public double Step4SoakTime { get; set; }
    public double Step4RampRate { get; set; }
    public double Step5Zone1Temp { get; set; }
    public double Step5Zone2Temp { get; set; }
    public double Step5SoakTime { get; set; }
    public double Step5RampRate { get; set; }
    public double ProcessZone1Safety { get; set; }
    public double ProcessZone2Safety { get; set; }
    public double ProcessBlower1 { get; set; }
    public double ProcessBlower2 { get; set; }
}
