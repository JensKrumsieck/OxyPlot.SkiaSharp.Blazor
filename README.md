# OxyPlot.SkiaSharp.Blazor
[![NuGet Badge](https://buildstats.info/nuget/OxyPlot.SkiaSharp.Blazor?includePreReleases=true)](https://www.nuget.org/packages/OxyPlot.SkiaSharp.Blazor/)
[![Maintainability](https://api.codeclimate.com/v1/badges/18aab64564a3aaa27138/maintainability)](https://codeclimate.com/github/JensKrumsieck/OxyPlot.SkiaSharp.Blazor/maintainability)
[![.NET](https://github.com/JensKrumsieck/OxyPlot.SkiaSharp.Blazor/actions/workflows/dotnet.yml/badge.svg)](https://github.com/JensKrumsieck/OxyPlot.SkiaSharp.Blazor/actions/workflows/dotnet.yml)
[![GitHub issues](https://img.shields.io/github/issues/JensKrumsieck/OxyPlot.SkiaSharp.Blazor)](https://github.com/JensKrumsieck/OxyPlot.SkiaSharp.Blazor/issues)
![GitHub commit activity](https://img.shields.io/github/commit-activity/y/JensKrumsieck/OxyPlot.SkiaSharp.Blazor)
[![GitHub license](https://img.shields.io/github/license/JensKrumsieck/OxyPlot.SkiaSharp.Blazor)](https://github.com/JensKrumsieck/OxyPlot.SkiaSharp.Blazor/blob/main/LICENSE)
![GitHub tag (latest by date)](https://img.shields.io/github/v/tag/jenskrumsieck/OxyPlot.SkiaSharp.Blazor)

The cross-platform plotting library - [OxyPlot](https://github.com/oxyplot/oxyplot) - is now available for Webassembly using Blazor and [SkiaSharp](https://github.com/mono/SkiaSharp).


<img src="https://github.com/JensKrumsieck/OxyPlot.SkiaSharp.Blazor/raw/main/.github/screen.png" alt="Screenshot" width="600" />

### [LIVE DEMO](https://blazor-playground.vercel.app/plot/)

### Installation
```
dotnet add package OxyPlot.SkiaSharp.Blazor
```

### Usage
```razor
<PlotView Model=model style="height: 30vh"/>
@code{
    private PlotModel model = new PlotModel();
    ...
    protected override async Task OnInitializedAsync()
    {
        var data = GetSomeDataPoints(); //get datapoint array from somewhere
        var spc = new LineSeries()
        {
            ItemsSource = data,                
            Title = "UV/Vis Data",
            TrackerFormatString = "{0}<br/>{1}: {2:0.00} - {3}: {4:0.00}"
        };
        model.Series.Add(spc);
    }
}
```

Requirements:
* .NET 6.0 (lower .NET versions are impossible due to a novelty introduced in 6.0)
* SkiaSharp.Views.Blazor v2.88.0-preview.179 or higher
* OxyPlot.SkiaSharp v2.1.0 or higher
