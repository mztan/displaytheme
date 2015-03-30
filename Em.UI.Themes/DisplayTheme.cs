using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;

namespace Em.UI.Themes
{
    public class DisplayTheme : DependencyObject
    {
        private const string ThemeUriPrefix = "ms-appdata:///theme/";
        private const string ThemeUriPrefix2 = "ms-appx:///theme/";

        private const string PackageFolder = "ms-appx:///Themes/";
        private const string LocalAppDataFolder = "ms-appdata:///local/Themes/";
        private const string RoamingAppDataFolder = "ms-appdata:///roaming/Themes/";

        private const string DefaultTheme = "Default";

        public static readonly DependencyProperty RequestedThemeProperty =
            DependencyProperty.RegisterAttached("RequestedTheme", typeof (string), typeof (DisplayTheme), new PropertyMetadata(null, RequestedThemeChanged));

        public static void SetRequestedTheme(UIElement element, string value)
        {
            element.SetValue(RequestedThemeProperty, value);
        }

        public static string GetRequestedTheme(UIElement element)
        {
            return (string) element.GetValue(RequestedThemeProperty);
        }

        /// <summary>
        /// Gets the list of all themes installed in the app.
        /// Themes can exist in four different places:
        ///  1) Application.Current.Resources.ThemeDictionaries
        ///  2) Package.Current.InstalledLocation
        ///  3) ApplicationData.Current.LocalFolder
        ///  4) ApplicationData.Current.RoamingFolder
        /// </summary>
        /// <returns>List of theme names</returns>
        public static async Task<IReadOnlyList<string>> GetThemesAsync()
        {
            var appResources = GetThemesFromApplicationResources();
            var appx = await GetThemesAsync(Package.Current.InstalledLocation).ConfigureAwait(false);
            var local = await GetThemesAsync(ApplicationData.Current.LocalFolder).ConfigureAwait(false);
            var roaming = await GetThemesAsync(ApplicationData.Current.RoamingFolder).ConfigureAwait(false);

            var themes = new List<string>();
            if (appx != null)
            {
                themes.AddRange(appx);
            }
            if (local != null)
            {
                themes.AddRange(local);
            }
            if (roaming != null)
            {
                themes.AddRange(roaming);
            }
            if (appResources != null)
            {
                themes.AddRange(appResources);
            }
            return themes;
        }

        /// <summary>
        /// Installs a theme to the app.
        /// </summary>
        /// <param name="dictionaryOrZipFile">
        /// Either a XAML file (.xaml) containing a ResourceDictionary, or a zip file containing a XAML file and other optional asset files.
        /// </param>
        /// <param name="roaming">Indicates whether this theme should be installed to ApplicationData.Current.RoamingFolder.</param>
        public static async Task InstallThemeAsync(IStorageFile dictionaryOrZipFile, bool roaming = false)
        {
            if (dictionaryOrZipFile == null)
                throw new ArgumentNullException("dictionaryOrZipFile");

            var destinationRoot = roaming ? ApplicationData.Current.RoamingFolder : ApplicationData.Current.LocalFolder;
            if (dictionaryOrZipFile.FileType == ".xaml")
            {
                await InstallThemeDictionaryAsync(dictionaryOrZipFile, destinationRoot).ConfigureAwait(false);
            }
            else if (dictionaryOrZipFile.FileType == ".xbf")
            {
                throw new NotSupportedException("XBF is not supported");
            }
            else
            {
                var isZip = await TryInstallThemeZipAsync(dictionaryOrZipFile, destinationRoot).ConfigureAwait(false);
                if (!isZip)
                {
                    throw new InvalidOperationException("invalid file");
                }
            }
        }

        private static void ApplyTheme(FrameworkElement element, string requestedTheme)
        {
            // If the element that is being themed is not a Frame, then we need to affect only
            // that element, and nothing else. However, since we need to make changes to Application.Current.Resources
            // in order to change the theme for that element, we need to undo those changes after the theme switch 
            // takes effect. So, we need to push the original values into a separate ResourceDictionary, make our
            // changes, and then pop the values back on to Application.Current.Resources after we're done.
            ResourceDictionary savedResources = null;
            if (!(element is Frame))
            {
                savedResources = new ResourceDictionary();
            }

            // Reset resources to default values
            var applicationResources = Application.Current.Resources;
            if (applicationResources.ThemeDictionaries.ContainsKey(DefaultTheme))
            {
                var defaultResources = applicationResources.ThemeDictionaries[DefaultTheme] as ResourceDictionary;
                if (defaultResources != null)
                {
                    foreach (var key in defaultResources.Keys)
                    {
                        if (savedResources != null && applicationResources.ContainsKey(key))
                        {
                            savedResources[key] = applicationResources[key];
                        }

                        applicationResources[key] = defaultResources[key];
                    }
                }
            }

            // If no requested theme, reset element to default theme and exit
            if (string.IsNullOrEmpty(requestedTheme) || requestedTheme == DefaultTheme)
            {
                element.RequestedTheme = ElementTheme.Light;
                element.RequestedTheme = ElementTheme.Default;
                return;
            }

            // Load resources for this theme
            var resources = LoadResources(requestedTheme);
            if (resources == null)
            {
                // Don't crash the designer!
                if (DesignMode.DesignModeEnabled)
                    return;

                throw new InvalidOperationException("invalid requested theme");
            }

            // Prepare conversion of URI strings and RelativeUris and AbsoluteUris to proper URIs
            string uriPrefix = Path.Combine(PackageFolder, requestedTheme);
            if (resources.Source != null)
            {
                var source = resources.Source.OriginalString;
                if (source.StartsWith(PackageFolder, StringComparison.OrdinalIgnoreCase))
                {
                    uriPrefix = Path.Combine(PackageFolder, requestedTheme);
                }
                else if (source.StartsWith(LocalAppDataFolder, StringComparison.OrdinalIgnoreCase))
                {
                    uriPrefix = Path.Combine(LocalAppDataFolder, requestedTheme);
                }
                else if (source.StartsWith(RoamingAppDataFolder, StringComparison.OrdinalIgnoreCase))
                {
                    uriPrefix = Path.Combine(RoamingAppDataFolder, requestedTheme);
                }
                else
                {
                    throw new InvalidOperationException("invalid source");
                }
            }

            // Apply themed resources
            foreach (var key in resources.Keys)
            {
                var resource = resources[key];

                // If resource is a string, check to see if it can be converted to an actual Uri.
                var resourceString = resource as string;
                if (resourceString != null)
                {
                    string relativePath = null;
                    if (resourceString.StartsWith(ThemeUriPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = resourceString.Substring(ThemeUriPrefix.Length);
                    }
                    else if (resourceString.StartsWith(ThemeUriPrefix2, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = resourceString.Substring(ThemeUriPrefix2.Length);
                    }

                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        resource = new Uri(Path.Combine(uriPrefix, relativePath));
                    }
                }
                else
                {
                    // If resource is a RelativeUri or AbsoluteUri, convert it to a Uri.
                    var relativeUri = resource as RelativeUri;
                    if (relativeUri != null)
                    {
                        resource = new Uri(Path.Combine(uriPrefix, relativeUri.Path));
                    }
                    else
                    {
                        var absoluteUri = resource as AbsoluteUri;
                        if (absoluteUri != null)
                        {
                            resource = new Uri(absoluteUri.Path);
                        }
                    }
                }

                if (savedResources != null && applicationResources.ContainsKey(key) && !savedResources.ContainsKey(key))
                {
                    savedResources[key] = applicationResources[key];
                }

                applicationResources[key] = resource;
            }

            // Affect UI
            object elementThemeObj;
            if (requestedTheme == "Light")
            {
                element.RequestedTheme = ElementTheme.Default;
                element.RequestedTheme = ElementTheme.Light;
            }
            else if (requestedTheme == "Dark")
            {
                element.RequestedTheme = ElementTheme.Default;
                element.RequestedTheme = ElementTheme.Dark;
            }
            else if (resources.TryGetValue("ElementTheme", out elementThemeObj) && elementThemeObj is ElementTheme)
            {
                // First, set the element's RequestedTheme to something else, to trigger UI update
                element.RequestedTheme = (ElementTheme) ((int) elementThemeObj != 0 ? (int) elementThemeObj - 1 : 1);

                // Then, set it to the requested value
                element.RequestedTheme = (ElementTheme) elementThemeObj;
            }
            else
            {
                element.RequestedTheme = ElementTheme.Dark;
                element.RequestedTheme = ElementTheme.Default;
            }

            // Pop resources from stack
            if (savedResources != null)
            {
                foreach (var key in savedResources.Keys)
                {
                    applicationResources[key] = savedResources[key];
                }
            }
        }

        private static ResourceDictionary LoadResources(string themeName)
        {
            var resources = LoadResourcesFromResources(themeName);

            if (resources == null)
            {
                resources = LoadResourcesFromPath(themeName, PackageFolder);
            }

            if (resources == null)
            {
                resources = LoadResourcesFromPath(themeName, LocalAppDataFolder);
            }

            if (resources == null)
            {
                resources = LoadResourcesFromPath(themeName, RoamingAppDataFolder);
            }

            return resources;
        }

        private static ResourceDictionary LoadResourcesFromResources(string themeName)
        {
            object resources;
            Application.Current.Resources.ThemeDictionaries.TryGetValue(themeName, out resources);
            return resources as ResourceDictionary;
        }

        private static ResourceDictionary LoadResourcesFromPath(string themeName, string path)
        {
            var uri = new Uri(Path.Combine(path, string.Format("{0}.xaml", themeName)));

            // If the file is in appdata, we cannot use ResourceDictionary.Source to load it,
            // for some unknown reason. We must read the XAML from the file and use XamlReader.Load.
            // A side effect to this is that we cannot load .xbf files.
            if (path.StartsWith("ms-appdata", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // Normally, setting ResourceDictionary.Source will cause the ResourceDictionary to 
                    // load the resources from the file specified by the source Uri. 
                    // However, for some unknown reason, because the file is in appdata, loading it causes
                    // an exception to be thrown.
                    // The correct workaround is to use XamlReader to load the ResourceDictionary. However,
                    // this results in a ResourceDictionary instance without its Source property set.
                    // This is a hack to both set the Source property and load its resources. The reason it 
                    // is a hack is because it relies on the undocumented behavior that setting the Source
                    // property causes the property itself to actually be set, despite ultimately throwing
                    // an exception.
                    var resources = new ResourceDictionary();

                    // We expect this operation to throw an exception
                    try { resources.Source = uri; }
                    catch
                    { }
                    // The postcondition to this operation is that Source is not null

                    // Because an exception was thrown, we cannot be sure about the state of the object, so
                    // we'll clear it out just to be safe.
                    resources.Clear();
                    resources.MergedDictionaries.Clear();
                    resources.ThemeDictionaries.Clear();

                    // Load resources from file
                    var file = StorageFile.GetFileFromApplicationUriAsync(uri).AsTask().Result;
                    var xaml = FileIO.ReadTextAsync(file).AsTask().Result;
                    var actualResources = XamlReader.Load(xaml) as ResourceDictionary;
                    if (actualResources == null)
                        return null;

                    // Copy resources over
                    foreach (var kvp in actualResources)
                    {
                        resources[kvp.Key] = kvp.Value;
                    }
                    foreach (var kvp in actualResources.ThemeDictionaries)
                    {
                        actualResources.ThemeDictionaries.Remove(kvp.Key);
                        resources.ThemeDictionaries[kvp.Key] = kvp.Value;
                    }
                    foreach (var dictionary in actualResources.MergedDictionaries)
                    {
                        actualResources.MergedDictionaries.Remove(dictionary);
                        resources.MergedDictionaries.Add(dictionary);
                    }

                    return resources;
                }
                catch (IOException)
                { }
            }

            try
            {
                return new ResourceDictionary { Source = uri };
            }
            catch
            { }

            return null;
        }

        private static async Task<IReadOnlyList<string>> GetThemesAsync(IStorageFolder rootFolder)
        {
            StorageFolder folder;
            try
            {
                folder = await rootFolder.GetFolderAsync("Themes").AsTask().ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            var files = await folder.GetFilesAsync().AsTask().ConfigureAwait(false);
            if (files == null || files.Count == 0)
                return new List<string>();

            var themeNames = new List<string>();
            foreach (var file in files)
            {
                if (file.FileType == ".xaml" || file.FileType == ".xbf")
                    themeNames.Add(Path.GetFileNameWithoutExtension(file.Name));
            }

            return themeNames;
        }

        // Must be called from UI thread!
        private static IReadOnlyList<string> GetThemesFromApplicationResources()
        {
            var themeDictionaries = Application.Current.Resources.ThemeDictionaries;
            return themeDictionaries.Keys.Cast<string>().Where(o => o != "Default").ToList();
        }

        private static async Task InstallThemeDictionaryAsync(IStorageFile dictionaryFile, IStorageFolder destinationRoot)
        {
            // Get themes folder
            var themesFolder = await destinationRoot.CreateFolderAsync("Themes", CreationCollisionOption.OpenIfExists)
                    .AsTask().ConfigureAwait(false);

            // Copy resource dictionary to themes folder
            await dictionaryFile.CopyAsync(themesFolder, dictionaryFile.Name, NameCollisionOption.ReplaceExisting)
                    .AsTask().ConfigureAwait(false);
        }

        private static async Task<bool> TryInstallThemeZipAsync(IStorageFile file, IStorageFolder destinationRoot)
        {
            try
            {
                using (var stream = await file.OpenStreamForReadAsync().ConfigureAwait(false))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    // Look for .xaml files
                    var xamlFiles = new List<string>();
                    foreach (var entry in zip.Entries)
                    {
                        if (Path.GetExtension(entry.Name) == ".xaml" && entry.Name == entry.FullName)
                        {
                            xamlFiles.Add(entry.FullName);
                        }
                    }

                    // If there are no XAML files, return false
                    if (xamlFiles.Count == 0)
                        return false;

                    // We define a theme to be defined by exactly one XAML file (plus any number of accompanying assets, like images, etc).
                    // Because the zip file may contain multiple XAML files, we need to resolve which file defines our theme.
                    string themeXaml = null;
                    if (xamlFiles.Count == 1)
                    {
                        // If the zip file contains exactly one XAML file, then we take that file to be our theme.
                        themeXaml = xamlFiles[0];
                    }
                    else
                    {
                        // If there are multiple XAML files, we look for the XAML file with the same name as the zip archive.
                        // If no such file exists, then we fail out.
                        foreach (var xaml in xamlFiles)
                        {
                            if (Path.GetFileNameWithoutExtension(xaml) == Path.GetFileNameWithoutExtension(file.Name))
                            {
                                themeXaml = file.Name;
                                break;
                            }
                        }
                    }

                    if (themeXaml == null)
                        return false;

                    // The theme name is name of the XAML file
                    string themeName = Path.GetFileNameWithoutExtension(themeXaml);
                    var themePathPrefix = string.Format("Themes\\{0}\\", themeName);

                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Name))
                            continue;

                        // Get destination folder
                        StorageFolder destinationFolder;
                        if (entry.FullName == themeXaml)
                        {
                            // The theme XAML file goes into the Themes folder
                            destinationFolder = await destinationRoot.CreateFolderAsync("Themes", CreationCollisionOption.OpenIfExists)
                                .AsTask().ConfigureAwait(false);
                        }
                        else
                        {
                            // All other files go into Themes\{themeName}
                            var path = Path.Combine(themePathPrefix, Path.GetDirectoryName(entry.FullName));
                            destinationFolder = await destinationRoot.CreateFolderAsync(path, CreationCollisionOption.OpenIfExists)
                                .AsTask().ConfigureAwait(false);
                        }

                        // Copy file to destination folder
                        var destinationFile = await destinationFolder.CreateFileAsync(entry.Name, CreationCollisionOption.ReplaceExisting)
                            .AsTask().ConfigureAwait(false);
                        using (var outStream = await destinationFile.OpenStreamForWriteAsync().ConfigureAwait(false))
                        using (var inStream = entry.Open())
                        {
                            await inStream.CopyToAsync(outStream).ConfigureAwait(false);
                        }
                    }

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static async Task CopyFolderAsync(IStorageFolder source, IStorageFolder destinationContainer, string desiredName = null)
        {
            var destinationFolder = await destinationContainer.CreateFolderAsync(
                desiredName ?? source.Name, CreationCollisionOption.ReplaceExisting);

            foreach (var file in await source.GetFilesAsync())
            {
                await file.CopyAsync(destinationFolder, file.Name, NameCollisionOption.ReplaceExisting);
            }
            foreach (var folder in await source.GetFoldersAsync())
            {
                await CopyFolderAsync(folder, destinationFolder);
            }
        }

        private static void RequestedThemeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            var requestedTheme = e.NewValue as string;
            ApplyTheme(element, requestedTheme);
        }
    }
}
