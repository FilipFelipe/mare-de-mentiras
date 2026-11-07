#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MareDeMentiras.EditorTools
{
    /// <summary>
    /// Garante nome, identificador e orientação do app em todas as plataformas (Mac, Windows, Android, WebGL).
    /// Roda ao abrir o projeto e também no CI (GitHub Actions), antes de cada build.
    /// Só altera o que estiver diferente.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectSettingsGuard
    {
        const string Product = "Maré de Mentiras";
        const string Company = "play-online-ff";
        const string AppId = "com.playonlineff.maredementiras";

        static ProjectSettingsGuard() => EditorApplication.delayCall += Apply;

        [MenuItem("Maré de Mentiras/Reaplicar configurações do projeto")]
        static void Apply()
        {
            bool changed = false;
            changed |= Set(PlayerSettings.productName, Product, v => PlayerSettings.productName = v);
            changed |= Set(PlayerSettings.companyName, Company, v => PlayerSettings.companyName = v);
            foreach (var target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.WebGL })
                changed |= Set(PlayerSettings.GetApplicationIdentifier(target), AppId, v => PlayerSettings.SetApplicationIdentifier(target, v));

            // O jogo é horizontal: no celular, só paisagem.
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation ||
                PlayerSettings.allowedAutorotateToPortrait || PlayerSettings.allowedAutorotateToPortraitUpsideDown ||
                !PlayerSettings.allowedAutorotateToLandscapeLeft || !PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = false;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[Maré de Mentiras] Configurações do projeto ajustadas ({AppId}).");
            }
        }

        static bool Set(string current, string wanted, System.Action<string> apply)
        {
            if (current == wanted) return false;
            apply(wanted);
            return true;
        }
    }
}
#endif
