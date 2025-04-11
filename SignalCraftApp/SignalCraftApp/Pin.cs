using System;
using System.ComponentModel;

public class Pin
{
    public int Id { get; set; }
    public string Name { get; set; }
    public SignalType SelectedType { get; set; }
    public string Value { get; set; }
    public SignalType[] SupportedSignals { get; set; }
    public string SelectedTypeDescription => GetEnumDescription(SelectedType);

    private static string GetEnumDescription(Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attribute = (DescriptionAttribute)Attribute.GetCustomAttribute(
            field,
            typeof(DescriptionAttribute));
        return attribute?.Description ?? value.ToString();
    }
}

public enum SignalType
{
    [Description("Не задано")]
    None,
    [Description("Дискретный")]
    Digital,
    [Description("Аналоговый")]
    Analog,
    [Description("ШИМ")]
    PWM,
    UART,
    SPI,
    I2C,
    [Description("PS/2")]
    PS2,
    VGA
}
