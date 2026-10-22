# BrimDeck — limited source adaptation

Upstream: [MoFeng2223/BrimDeck](https://github.com/MoFeng2223/BrimDeck)

Pinned revision: `e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3` (reviewed 2026-10-22).

License: Apache-2.0. The upstream root license is preserved unchanged as [LICENSE](LICENSE).

Adapted source: [MusicVisuals.cs](https://github.com/MoFeng2223/BrimDeck/blob/e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3/src/BrimDeck/MusicVisuals.cs), previous/next vector contours only.

GlassDesk destination: `src/GlassDesk.App/SkipGlyphVisual.cs`.

Changes: replace WPF Geometry/Viewbox rendering with retained Windows.UI.Composition geometry; sample quadratic curves; mirror the previous contour to form next; apply existing GlassDesk ink, disabled states and button sizing. Upstream WPF classes, executable files, fonts, application logos and other assets are not included.

Upstream's dependency notices were inspected. This adaptation introduces no BrimDeck package dependencies. The root license contains an unfilled copyright example; this notice does not invent a copyright year or rights holder beyond identifying the upstream project/author account.

The NetEase CDP bridge and settings UI were researched only. They are not bundled or activated by this change. No endorsement by the upstream author is implied.
