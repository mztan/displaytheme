# displaytheme
Go beyond Light and Dark themes in your universal Windows app with this handy library! 

Features
-------
1. Allow the user to choose from an unlimited number of themes! No more need to limit your app to just Light and Dark.
2. Change themes dynamically, without requiring the user to restart the app.
3. Themes can be baked in at compile-time or installed dynamically via the web.
4. Easy to use! Keep your code short and simple!

Usage
-------
1.  Depending on your needs, themes can be installed to any of four locations:
    * App.xaml: simply add a `ResourceDictionary` to `Application.Resources.ThemeDictionaries`. 
      For example:


          <Application x:Class="Sample.App">
              <Application.Resources>
                  <ResourceDictionary>
                      <ResourceDictionary.ThemeDictionaries>
                          <ResourceDictionary x:Key="MyTheme">
                              ...
                          </ResourceDictionary>
                      </ResourceDictionary.ThemeDictionaries>
                  </ResourceDictionary>
              </Application.Resources>
          </Application>

    * Loose XAML file in your app package. This file must reside in `ms-appx:///Themes/`. Other assets 
      related to the theme (e.g. image files) must reside in `ms-appx:///Themes/{theme-name}/`.
      For example: `ms-appx:///Themes/MyTheme.xaml`, `ms-appx:///Themes/MyTheme/logo.png`.
    * Loose XAML file in `ApplicationData.Current.LocalFolder`. Accompanying assets must 
      reside in `ms-appdata:///local/Themes/`.
    * Loose XAML file in `ApplicationData.Current.RoamingFolder`. Accompanying assets must 
      reside in `ms-appdata:///roaming/Themes/`.
2.  Themes are identified by either their `x:Key`, if installed to App.xaml, or by their name, 
    if installed as a XAML file. Note that theme names must be unique!
3.  When referencing an asset from a theme resource, use the provided `RelativeUri` class. In advanced
    scenarios, you can also use the `AbsoluteUri` class. These classes are necessary because there is
    no native way to declare a `Uri` in XAML. `RelativeUri` and `AbsoluteUri` will be automatically 
    converted into real `Uri` instances when the theme is applied. (Note that the XAML designer will 
    complain about the type of `RelativeUri`/`AbsoluteUri`, but that's OK, the binding will work 
    correctly at runtime). E.g.


        MainPage.xaml:
        <Image Source="{ThemeResource ImageUri}"/>
        
        MyTheme.xaml:
        <themes:RelativeUri x:Key="ImageUri">logo.png</themes:RelativeUri>

    Depending on where the theme was installed to, `logo.png` will resolve either resolve to 
    `ms-appx:///Themes/MyTheme/logo.png`, `ms-appdata:///local/Themes/MyTheme/logo.png`, or
    `ms-appdata:///roaming/Themes/MyTheme/logo.png`.
4.  If you wish to force a XAML element to a particular theme, you can use the `DisplayTheme.RequestedTheme`
    attached property. This is particularly useful when showing a list of all themes installed in the app.


        <ListView ItemsSource="{Binding InstalledThemes}">
            <ListView.ItemTemplate>
                <DataTemplate>
                    <Grid themes:DisplayTheme.RequestedTheme="{Binding}">
                        ...
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>

