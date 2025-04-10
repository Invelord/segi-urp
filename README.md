# Sonic Ether Global Illumination URP (SEGI but for URP)
![Screenshot](Thumbnail.gif)

<div align="justify">SEGI URP is a fully dynamic global illumination which includes both indirect diffuse gi and reflections. SEGI URP utilize voxel based approach with cone tracing. This project is not production ready and is never intended to be a fully finished project, therefore feel free to 'hack' around with the code yourself because we do not have the time to maintain / improve this project. We are NOT active in this github repository, therefore if you want to have a chat with us, feel free to join our discord server instead.</div>

<h2>Breakdown Summary</h2>

![Screenshot](voxel-gi-process.jpg)
<div align="justify">SEGI URP use the following process. Firstly, external cameras are deployed to render voxels and voxel shadow map. To support forward/forward+ rendering, we added a custom renderer feature which grabs the required gbuffers (e.g. albedo, specular, etc). Next, to keep things simple, a fullscreen shader is used to trace the voxel and compute both reflections and indirect diffuse gi. This process is often done in separate passes, however for simplicity, we combined them in one fullscreen shadergraph. Temporal and Spatial denoiser can be used if we add random sampling, however we were too lazy to make one. We aimed to make things more user-friendly but in the end we end up with more complex shaders and very very confusing which led us to basically pressed the 'surrender' button. The above are some screenshots of various textures / buffers / whatever you want to call them which contributes to the final result of the global illumination.</div>

<h2>Features</h2>
<ol>
  <li>World Space Indirect Diffuse GI through Voxel Cone Tracing.</li>
  <li>World Space Specular Reflections through Voxel Cone Tracing.</li>
  <li>Supports both static and dynamic objects (particle systems too).</li>
  <li>Supports directional light and emissive lights / meshes.</li>
  <li>Does not require RTX hardware.</li>
  <li>Supports Forward, Forward+, and Deferred Rendering.</li>
</ol>

<h2>Limitations</h2>
<ol>
  <li>Not beginner-friendly.</li>
  <li>Only supports URP (Universal Render Pipeline).</li>
  <li>Render Graph is not yet supported (Needs to enable compatibility mode).</li>
  <li>Does not support point lights, spot lights, area lights. (Please use emissive meshes instead).</li>
  <li>Does not support mobile.</li>
  <li>Very heavy in terms of performance.</li>
  <li>Light Leaking.</li>
</ol>

<h2>Credits</h2>
<ul>
  <li>https://github.com/sonicether/SEGI</li>
  <li>https://github.com/jiaozi158/UnitySSPathTracingURP</li>
</ul>
