using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(TeamMaskAttribute))]
public class TeamMaskDrawer : PropertyDrawer
{
    private static readonly string[] _teamNames = BuildTeamNames();

    private static string[] BuildTeamNames()
    {
        var names = new string[32];
        for (int i = 0; i < 32; i++)
            names[i] = $"Team {i}";
        return names;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        property.intValue = EditorGUI.MaskField(position, label, property.intValue, _teamNames);
        EditorGUI.EndProperty();
    }
}