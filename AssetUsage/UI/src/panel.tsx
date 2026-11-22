import { ReactNode, useState } from "react";
import { Button, Scrollable, Tooltip } from "cs2/ui";
import { actions, ModNode, PrefabNode, TypeNode, useOpen, useTree } from "./bindings";
import styles from "./panel.module.scss";

const ICON = "coui://assetusage/icon.svg";

export const LoadIssuesButton = () => {
    const open = useOpen();
    const tree = useTree();
    if (!tree.scanned && !tree.error) return null;
    return (
        <>
            <Tooltip tooltip={`Load issues: ${tree.total ?? 0}`} direction="down" delayTime={0}>
                <Button variant="floating" src={ICON} selected={open} onSelect={() => actions.setOpen(!open)} />
            </Tooltip>
            {open && <Panel />}
        </>
    );
};

const Panel = () => {
    const tree = useTree();
    const mods = tree.mods ?? [];
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
            {!tree.error && mods.length === 0 && broken === 0 && <div className={styles.empty}>No issues found.</div>}
            <Scrollable className={styles.list} vertical trackVisibility="scrollable">
                {broken > 0 && (
                    <div className={styles.row}>
                        <div className={styles.name}>Broken nodes</div>
                        <Stepper cursor={tree.brokenCursor ?? 0} count={broken} onStep={actions.stepNode} />
                    </div>
                )}
                {mods.map((mod) => <ModRow key={mod.name} mod={mod} />)}
            </Scrollable>
        </div>
    );
};

const Branch = ({ label, count, level, children }: { label: string; count: number; level: number; children: ReactNode }) => {
    const [expanded, setExpanded] = useState(false);
    return (
        <>
            <div className={`${styles.row} ${styles.branch}`} style={{ paddingLeft: `${8 + level * 16}rem` }} onClick={() => setExpanded(!expanded)}>
                <div className={styles.arrow}>{expanded ? "▾" : "▸"}</div>
                <div className={styles.name}>{label}</div>
                <div className={styles.count}>{count}</div>
            </div>
            {expanded && children}
        </>
    );
};

const ModRow = ({ mod }: { mod: ModNode }) => (
    <Branch label={mod.name} count={mod.count} level={0}>
        {mod.types.map((type) => <TypeRow key={type.category} type={type} />)}
    </Branch>
);

const TypeRow = ({ type }: { type: TypeNode }) => (
    <Branch label={type.category} count={type.count} level={1}>
        {type.prefabs.map((prefab) => <PrefabRow key={prefab.id} prefab={prefab} />)}
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
        <div className={styles.position}>{cursor}/{count}</div>
        <button className={styles.icon} onClick={() => onStep(1)}>▶</button>
    </div>
);
