# Sonic Ether Global Illumination URP (SEGI but for URP)
![Screenshot](Thumbnail.gif)

SEGI URP is a fully dynamic global illumination which includes both indirect diffuse gi and reflections. SEGI URP utilize voxel based approach with cone tracing therefore producing noise-free global illumination and also does not need to rely on temporal denoiser. Please watch the video guide for better understanding on the installation process. This project is a hobby project and due to very limited free time available, we are not always online all the time to help with fixing issues.

Features:
1. World Space Indirect Diffuse GI through Voxel Cone Tracing.
2. World Space Specular Reflections through Voxel Cone Tracing.
3. Supports both static and dynamic objects (particle systems too).
4. Supports directional light and emissive lights / meshes.
6. Does not require RTX hardware.
7. Supports Deferred Rendering by default (Forward/Forward+ requires extra setup).

Limitations:
1. Only supports URP (Universal Render Pipeline).
2. Render Graph is not yet supported (Needs to enable compatibility mode).
3. Does not support point lights, spot lights, area lights. (Please use emissive meshes instead).
4. Does not support mobile.
5. Not beginner-friendly.
6. Light Leaking in some areas.

Credits:
- https://github.com/sonicether/SEGI
