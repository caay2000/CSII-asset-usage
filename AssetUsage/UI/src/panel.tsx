import { ReactNode, useState } from "react";
import { Button, Scrollable, Tooltip } from "cs2/ui";
import { actions, IssueGroup, ModNode, PrefabNode, SurfaceOption, useNotice, useOpen, useSurfaces, useTree } from "./bindings";
import styles from "./panel.module.scss";

const ICON = "coui://assetusage/icon.svg";

export const LoadIssuesButton = () => {
    const open = useOpen();
    const notice = useNotice();
    const tree = useTree();
    if (!tree.scanned && !tree.error) return null;
    return (
        <>
            <Tooltip tooltip={`Load issues: ${tree.total ?? 0}`} direction="down" delayTime={0}>
                <Button variant="floating" src={ICON} selected={open} onSelect={() => actions.setOpen(!open)} />
            </Tooltip>
            {notice && !open && <Notice total={tree.total ?? 0} />}
            {open && <Panel />}
        </>
    );
};

const Notice = ({ total }: { total: number }) => (
    <div className={styles.notice}>
        <div className={styles.noticeText}>This city has {total} load issues.</div>
        <button className={styles.action} onClick={() => actions.setOpen(true)}>Open</button>
        <button className={styles.action} onClick={() => actions.dismissNotice()}>Skip</button>
    </div>
);

const Panel = () => {
    const tree = useTree();
    const groups = tree.groups ?? [];
    const broken = tree.brokenNodes ?? 0;
    return (
        <div className={styles.panel}>
            <div className={styles.header}>
                <div className={styles.title}>Load issues</div>
                <div className={styles.total}>{tree.total ?? 0}</div>
                <button className={styles.action} onClick={() => actions.rescan()}>Rescan</button>
                <button className={styles.icon} onClick={() => actions.setOpen(false)}>×</button>
            </div>
            {tree.error && <div className={styles.error}>{tree.error}</div>}
            {tree.result && <div className={styles.result}>{tree.result}</div>}
            {!tree.error && groups.length === 0 && broken === 0 && <div className={styles.empty}>No issues found.</div>}
            <Scrollable className={styles.list} vertical trackVisibility="scrollable">
                {broken > 0 && (
                    <>
                        <div className={styles.row}>
                            <div className={styles.arrow} />
                            <div className={styles.name}>Broken nodes</div>
                            <div className={styles.count}>{broken}</div>
                            <Stepper cursor={tree.brokenCursor ?? 0} count={broken} onStep={actions.stepNode} />
                        </div>
                        <div className={styles.kept}>
                            {(tree.brokenKinds ?? []).map((kind) => `${kind.name}: ${kind.count}`).join(", ")}
                            {tree.brokenCurrent ? `. Current: ${tree.brokenCurrent}` : ""}
                        </div>
                    </>
                )}
                {groups.map((group) => <GroupRow key={group.category} group={group} />)}
            </Scrollable>
        </div>
    );
};

const Branch = ({ label, count, level, children }: { label: string; count: number; level: number; children: ReactNode }) => {
    const [expanded, setExpanded] = useState(false);
    return (
        <>
            <div className={`${styles.row} ${styles.branch}`} style={{ paddingLeft: `${8 + level * 16}rem` }} onClick={() => setExpanded(!expanded)}>
                <div className={styles.arrow}>{expanded ? "▼" : "▶"}</div>
                <div className={styles.name}>{label}</div>
                <div className={styles.count}>{count}</div>
            </div>
            {expanded && children}
        </>
    );
};

const GroupRow = ({ group }: { group: IssueGroup }) => (
    <Branch label={group.category} count={group.count} level={0}>
        {group.fixes.length > 0 && group.count > 0 && <FixBar group={group} />}
        {group.mods.map((mod) => <ModRow key={mod.name} mod={mod} />)}
    </Branch>
);

type Pending = { text: string; run: () => void } | null;

const FixBar = ({ group }: { group: IssueGroup }) => {
    const [pending, setPending] = useState<Pending>(null);
    const [choosing, setChoosing] = useState(false);
    const confirm = (text: string, run: () => void) => {
        setChoosing(false);
        setPending({ text, run });
    };
    if (pending) {
        return (
            <div className={styles.fix}>
                <div className={styles.fixText}>{pending.text} This cannot be undone once you save.</div>
                <button className={styles.action} onClick={() => { pending.run(); setPending(null); }}>Confirm</button>
                <button className={styles.action} onClick={() => setPending(null)}>Cancel</button>
            </div>
        );
    }
    return (
        <>
            <div className={styles.fix}>
                {group.fixes.includes("changeBrand") && (
                    <button className={styles.action} onClick={() => confirm("Change the missing brand of every company that has a compatible brand loaded?", actions.changeBrands)}>Change all brands</button>
                )}
                {group.fixes.includes("remove") && (
                    <button className={styles.action} onClick={() => group.category === "Object"
                        ? confirm("Remove every missing object that has a position? Buildings, road objects, pillars and utilities are kept.", actions.removeObjects)
                        : confirm("Remove every missing surface, including those inside buildings?", actions.removeSurfaces)}>Remove all</button>
                )}
                {group.fixes.includes("replace") && (
                    <button className={styles.action} onClick={() => setChoosing(!choosing)}>Replace all</button>
                )}
            </div>
            {choosing && <SurfacePicker onPick={(surface) => confirm(`Replace every missing surface, including those inside buildings, with ${surface.name}?`, () => actions.replaceSurfaces(surface.id))} />}
        </>
    );
};

const SurfacePicker = ({ onPick }: { onPick: (surface: SurfaceOption) => void }) => {
    const surfaces = useSurfaces();
    return (
        <Scrollable className={styles.picker} vertical trackVisibility="scrollable">
            {surfaces.map((surface) => (
                <div key={surface.id} className={`${styles.row} ${styles.branch}`} onClick={() => onPick(surface)}>
                    <div className={styles.name}>{surface.name}</div>
                </div>
            ))}
        </Scrollable>
    );
};

const ModRow = ({ mod }: { mod: ModNode }) => (
    <Branch label={mod.name} count={mod.count} level={1}>
        {mod.prefabs.map((prefab) => <PrefabRow key={prefab.id} prefab={prefab} />)}
    </Branch>
);

const PrefabRow = ({ prefab }: { prefab: PrefabNode }) => (
    <div className={styles.prefab}>
        <div className={styles.row}>
            <div className={styles.name}>{prefab.name}</div>
            {prefab.unlocated > 0 && <div className={styles.note}>{prefab.unlocated} no location</div>}
            {prefab.located > 0 && <Stepper cursor={prefab.cursor} count={prefab.located} onStep={(delta) => actions.step(prefab.id, delta)} />}
        </div>
        {prefab.keptAliveBy.length > 0 && <div className={styles.kept}>Kept alive by {prefab.keptAliveBy.join(", ")}</div>}
    </div>
);

const Stepper = ({ cursor, count, onStep }: { cursor: number; count: number; onStep: (delta: number) => void }) => (
    <div className={styles.stepper}>
        <button className={styles.icon} onClick={() => onStep(-1)}>◀</button>
        <div className={styles.position}>{`${cursor}/${count}`}</div>
        <button className={styles.icon} onClick={() => onStep(1)}>▶</button>
    </div>
);
