$targets = @('D:\RimWorld_HSK\Mods\HSKDietTracker', 'D:\RimWorld_HSK_1.5\Mods\HSKDietTracker')
$source = 'D:\Mods\HSKDietTracker'
foreach ($t in $targets) {
    if (Test-Path $t) { cmd /c rmdir "$t" }
    cmd /c mklink /J "$t" "$source"
}
