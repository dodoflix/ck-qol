"""Dump the game's GameObject hierarchies and sprites out of the shipped data files.

MonoBehaviour typetrees are stripped from the shipped build, so a script's own
serialized field values cannot be read here - only the hierarchy and the component
class names come out. The same stripping makes a normal read of a MonoBehaviour
throw ("Expected to read N bytes, but only read M"), so m_Script is taken from the
raw bytes instead.

Run through scripts/dump.sh, which supplies the interpreter and UnityPy.
"""
import os, struct, sys, UnityPy

DATA, OUT = sys.argv[1], sys.argv[2]
TREES, SPRITES = os.path.join(OUT, "assets"), os.path.join(OUT, "sprites")

# m_GameObject (PPtr, 12 bytes) + m_Enabled (1 byte, padded to 4), then m_Script.
SCRIPT_OFFSET = 16


def sources():
    """Every serialized file the game ships, addressable bundles included.

    The .resS files hold the sprite pixels and have to be handed over too, or every
    sprite outside a bundle fails with "Resource file resources.assets.resS not
    found" - UnityPy resolves them out of what the environment was loaded with, not
    off disk beside the file that references them.
    """
    for name in sorted(os.listdir(DATA)):
        if (name.endswith((".assets", ".resS", ".resource"))
                or (name.startswith("level") and name[5:].isdigit())):
            yield os.path.join(DATA, name)
    bundles = os.path.join(DATA, "StreamingAssets", "aa", "StandaloneWindows64")
    if os.path.isdir(bundles):
        for name in sorted(os.listdir(bundles)):
            if name.endswith(".bundle"):
                yield os.path.join(bundles, name)


def origin(obj, file_id):
    """File a PPtr points into: 0 is the pointer's own file, the rest are externals."""
    if file_id == 0:
        return os.path.basename(obj.assets_file.name)
    externals = obj.assets_file.externals
    if file_id > len(externals):
        return None
    return os.path.basename(externals[file_id - 1].name)


def script_name(obj, scripts):
    """Class behind a MonoBehaviour, or None if it lives outside what was loaded."""
    try:
        raw = obj.get_raw_data()
        endian = getattr(getattr(obj, "reader", None), "endian", "<")
        file_id, path_id = struct.unpack_from(endian + "iq", raw, SCRIPT_OFFSET)
    except Exception:
        return None
    return scripts.get((origin(obj, file_id), path_id))


def collect(objects, scripts):
    """GameObjects with their component names, and the transform links between them."""
    gameobjects, owner_of, father_of = {}, {}, {}
    for o in objects:
        if o.type.name != "GameObject":
            continue
        try:
            d = o.read()
        except Exception:
            continue
        key = (os.path.basename(o.assets_file.name), o.path_id)
        names = []
        for c in d.m_Component:
            ptr = getattr(c, "component", c)
            try:
                comp = ptr.deref()
            except Exception:
                names.append("?")
                continue
            name = comp.type.name
            if name == "MonoBehaviour":
                name = script_name(comp, scripts) or "MonoBehaviour?"
            elif name in ("Transform", "RectTransform"):
                owner_of[(os.path.basename(comp.assets_file.name), comp.path_id)] = key
                try:
                    father = comp.read().m_Father
                    if father.path_id:
                        father_of[key] = (origin(comp, father.file_id), father.path_id)
                except Exception:
                    pass
            names.append(name)
        gameobjects[key] = (d.m_Name, names)
    return gameobjects, owner_of, father_of


def write_trees(gameobjects, owner_of, father_of, filenames):
    # A father is a Transform; the tree is over the GameObjects that own them.
    parent, children = {}, {}
    for key, tkey in father_of.items():
        owner = owner_of.get(tkey)
        if owner is None or owner == key:
            continue
        parent[key] = owner
        children.setdefault(owner, []).append(key)

    roots = {}
    for key in gameobjects:
        if key not in parent:
            roots.setdefault(key[0], []).append(key)

    def label(key):
        return gameobjects[key][0] or ""

    os.makedirs(TREES, exist_ok=True)
    total = 0
    for name, keys in sorted(roots.items()):
        lines = []
        stack = [(k, 0) for k in sorted(keys, key=label, reverse=True)]
        seen = set()
        while stack:
            key, depth = stack.pop()
            if key in seen:          # a cycle would otherwise hang the walk
                continue
            seen.add(key)
            go_name, comps = gameobjects[key]
            pad = "  " * depth
            lines.append("%s%s [%d]" % (pad, go_name or "<unnamed>", key[1]))
            lines.extend("%s  +%s" % (pad, c) for c in comps)
            stack.extend((c, depth + 1)
                         for c in sorted(children.get(key, []), key=label, reverse=True))
        with open(os.path.join(TREES, filenames.get(name, name) + ".tree.txt"), "w") as fh:
            fh.write("\n".join(lines) + "\n")
        total += len(seen)
    print("  %d trees, %d objects" % (len(roots), total), flush=True)


def write_sprites(objects):
    """One PNG per Sprite, named the way the game's code refers to it."""
    os.makedirs(SPRITES, exist_ok=True)
    used, ok, failed = set(), 0, 0
    for o in objects:
        if o.type.name != "Sprite":
            continue
        try:
            d = o.read()
            name = (d.m_Name or "unnamed").replace("/", "_")
            if name in used:         # names repeat across equipment sets
                name = "%s_%d" % (name, o.path_id)
            used.add(name)
            d.image.save(os.path.join(SPRITES, name + ".png"))
            ok += 1
        except Exception:
            failed += 1
    print("  %d sprites%s" % (ok, ", %d unreadable" % failed if failed else ""), flush=True)


def main():
    paths = list(sources())
    print("loading %d files..." % len(paths), flush=True)
    env = UnityPy.load(*paths)
    objects = list(env.objects)

    scripts, filenames = {}, {}
    for o in objects:
        # Inside a bundle the serialized file is called CAB-<hash>; name the tree after
        # the bundle it came out of instead.
        bundle = getattr(getattr(o.assets_file, "parent", None), "name", None)
        if bundle:
            filenames[os.path.basename(o.assets_file.name)] = bundle
        if o.type.name == "MonoScript":
            try:
                scripts[(os.path.basename(o.assets_file.name), o.path_id)] = o.read().m_ClassName
            except Exception:
                pass
    print("%d objects, %d scripts" % (len(objects), len(scripts)), flush=True)

    write_trees(*collect(objects, scripts), filenames)
    write_sprites(objects)


main()
