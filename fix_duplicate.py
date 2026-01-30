import re

# Read the file
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()

# Remove duplicate line 1415
if len(lines) > 1415:
    if 'String truncated' in lines[1414] and 'String truncated' in lines[1415]:
        del lines[1415]

# Write back
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'w', encoding='utf-8') as f:
    f.writelines(lines)

print("Removed duplicate line!")
